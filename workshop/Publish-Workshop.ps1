#Requires -Version 7.2
param([ValidateSet('Create','Publish','Verify')][string]$Action='Verify',[Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference='Stop'
$previousSteamAppId=$env:SteamAppId
$previousPath=$env:PATH
$env:PATH=$GameDir+[IO.Path]::PathSeparator+$previousPath
$env:SteamAppId='602960'
$folder=Join-Path $PSScriptRoot 'content/EmoteChat'
$statePath=Join-Path $PSScriptRoot 'publish-state.json'
$title=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'title.zh-CN.txt')).Trim()
$description=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'description.zh-CN.txt')).Trim()
$preview=Join-Path $PSScriptRoot 'cover.jpg'
$contentId='barotrauma-chat-with-emote-v01'
if(-not(Test-Path -LiteralPath (Join-Path $folder 'filelist.xml'))){throw 'Public package missing'}
if((Get-Item -LiteralPath $preview).Length -ge 1048576){throw 'Preview is too large'}
if(Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object {$_.Extension -match '^\.(dll|log|save|sub|nettrace|etlx|ecp)$' -or $_.Name -eq 'names.json' -or ($_.Extension -eq '.exe' -and $_.FullName -ne (Join-Path $folder 'Tools/EmoteImporter.exe'))}){throw 'Unexpected binary/private data in public package'}
$sdk=[Reflection.Assembly]::LoadFrom((Join-Path $GameDir 'Facepunch.Steamworks.Win64.dll'))
try {
    [Steamworks.SteamClient]::Init(602960,$true)
    if(-not[Steamworks.SteamClient]::IsLoggedOn -or [uint][Steamworks.SteamClient]::AppId -ne 602960){throw 'Expected logged-on Barotrauma Steam session'}
    if($Action -eq 'Create') {
        if(Test-Path -LiteralPath $statePath){throw 'A publication state already exists; inspect it instead of creating duplicates'}
        $query=[Steamworks.Ugc.Query]::Items
        $query=$query.WhereUserPublished([Steamworks.SteamClient]::SteamId)
        $page=$query.GetPageAsync(1).WaitAsync([TimeSpan]::FromSeconds(45)).GetAwaiter().GetResult()
        if(-not$page){throw 'Could not check existing owned publications'}
        try {
            if($page.TotalCount -gt $page.ResultCount){throw 'Existing items span multiple pages; inspect all pages before creating'}
            if(@($page.Entries | Where-Object {$_.Title -match 'chat with emote|表情包聊天和气泡显示'}).Count){throw 'Potential existing chat-with-emote publication; inspect before creating'}
        } finally {$page.Dispose()}
        $editor=[Steamworks.Ugc.Editor]::NewCommunityFile
        $editor=$editor.WithTitle($title).WithDescription($description).WithVisibility([Steamworks.Ugc.Visibility]::Private)
        $result=$editor.SubmitAsync($null,$null).WaitAsync([TimeSpan]::FromSeconds(120)).GetAwaiter().GetResult()
        if(-not$result.Success){throw ('Create failed: '+$result.Result)}
        $state=[ordered]@{project=$contentId;file_id=[string]$result.FileId;title=$title;app_id=602960;status='created_private';needs_workshop_agreement=[bool]$result.NeedsWorkshopAgreement;created_utc=[DateTime]::UtcNow.ToString('o')}
        $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8
        Write-Output ('Created private draft '+$result.FileId+'; needs_agreement='+$result.NeedsWorkshopAgreement)
        if($result.NeedsWorkshopAgreement){throw 'Steam requires the Workshop agreement. Accept it, keep publish-state.json, then resume this same item.'}
        return
    }
    $state=Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json -AsHashtable
    if($state.project -ne $contentId -or $state.app_id -ne 602960){throw 'Unexpected publication state'}
    $id=[Steamworks.Data.PublishedFileId]::op_Implicit([ulong]$state.file_id)
    $item=[Steamworks.Ugc.Item]::GetAsync($id,0).WaitAsync([TimeSpan]::FromSeconds(45)).GetAwaiter().GetResult()
    if(-not$item -or $item.Result -ne [Steamworks.Result]::OK){throw 'Unable to verify workshop item'}
    if($item.Owner.Id -ne [Steamworks.SteamClient]::SteamId -or [uint]$item.ConsumerApp -ne 602960){throw 'Target is not owned by the current Barotrauma account'}
    if($Action -eq 'Publish') {
        $xml=[xml][IO.File]::ReadAllText((Join-Path $folder 'filelist.xml'))
        $xml.contentpackage.SetAttribute('steamworkshopid',$state.file_id)
        $xml.Save((Join-Path $folder 'filelist.xml'))
        $editor=[Steamworks.Ugc.Editor]::new($id)
        $editor=$editor.InLanguage('schinese').WithTitle($title).WithDescription($description).WithContent($folder).WithPreviewFile($preview)
        $editor=$editor.WithTag('Client-side').WithTag('Server-side').WithVisibility([Steamworks.Ugc.Visibility]::Public)
        $editor=$editor.WithMetaData($contentId).WithChangeLog('Initial preview: local emotes, GIF chat, overhead bubbles')
        $result=$editor.SubmitAsync($null,$null).WaitAsync([TimeSpan]::FromSeconds(240)).GetAwaiter().GetResult()
        $state['last_submit_result']=[string]$result.Result
        $state['needs_workshop_agreement']=[bool]$result.NeedsWorkshopAgreement
        $state['status']=if($result.Success){'uploaded_public'}else{'upload_failed'}
        $state['submitted_utc']=[DateTime]::UtcNow.ToString('o')
        $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8
        if(-not$result.Success){throw ('Upload failed: '+$result.Result)}
        if($result.NeedsWorkshopAgreement){throw 'Steam requires the Workshop agreement. Keep the item ID and resume after accepting it.'}
        Write-Output ('Upload result='+$result.Result+'; item='+$result.FileId+'; needs_agreement='+$result.NeedsWorkshopAgreement)
        $item=[Steamworks.Ugc.Item]::GetAsync($id,0).WaitAsync([TimeSpan]::FromSeconds(45)).GetAwaiter().GetResult()
        $dependency=[Steamworks.Data.PublishedFileId]::op_Implicit([ulong]2559634234)
        $added=$item.AddDependency($dependency).WaitAsync([TimeSpan]::FromSeconds(45)).GetAwaiter().GetResult()
        $state['luacs_dependency_added']=[bool]$added
        $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8
        Write-Output ('LuaCs prerequisite linked='+$added)
    }
    $item=[Steamworks.Ugc.Item]::GetAsync($id,0).WaitAsync([TimeSpan]::FromSeconds(45)).GetAwaiter().GetResult()
    $verification=[ordered]@{id=[string]$item.Id;title=$item.Title;visibility=[string]$item.Visibility;result=[string]$item.Result;banned=$item.IsBanned;bytes=$item.SizeOfFileInBytes;preview=$item.PreviewImageUrl;tags=$item.Tags;description_matches=($item.Description -eq $description);url=$item.Url;updated_utc=$item.Updated.ToUniversalTime().ToString('o')}
    $verification | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Encoding utf8
    $verification | ConvertTo-Json -Depth 5
} finally {if([Steamworks.SteamClient]::IsValid){[Steamworks.SteamClient]::Shutdown()};$env:SteamAppId=$previousSteamAppId;$env:PATH=$previousPath}
