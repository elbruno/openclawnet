#requires -Version 7
<#
.SYNOPSIS
  Render OpenClawNet session slides with Marp and inject the theme-switcher widget.

.DESCRIPTION
  Wraps `marp-cli` and post-processes the generated HTML to add a fixed-position
  ☀️ Light / 🌙 Dark / 💻 System switcher (top-right). The selection is stored
  in localStorage('oc-theme') and applied as `html.theme-light` / `html.theme-dark`,
  which the openclaw.css theme picks up to override the system preference.

  Per-slide `<!-- _class: light/dark -->` overrides still win over the global setting.

.EXAMPLE
  pwsh scripts/render-slides.ps1                                  # all sessions, all variants
  pwsh scripts/render-slides.ps1 -Sessions session-2              # one session
  pwsh scripts/render-slides.ps1 -Sessions session-1,session-2    # several
  pwsh scripts/render-slides.ps1 -Variants slides-es              # only Spanish variants
#>
param(
  [string[]]$Sessions,
  [string[]]$Variants = @('slides','slides-es'),
  [string]$ThemeCss = "docs/sessions/_theme/openclaw.css",
  [string]$Root     = "docs/sessions"
)

$ErrorActionPreference = 'Stop'

$switcherHtml = @'
<div id="oc-theme-switcher" role="group" aria-label="Color theme">
  <button data-theme="light"  title="Light" aria-label="Light">☀️</button>
  <button data-theme="dark"   title="Dark"  aria-label="Dark">🌙</button>
  <button data-theme="system" title="System (default)" aria-label="System">💻</button>
</div>
<style id="oc-theme-switcher-style">
#oc-theme-switcher{
  position:fixed;top:12px;right:12px;z-index:9999;
  display:inline-flex;gap:4px;padding:4px;
  background:rgba(127,127,127,.12);
  backdrop-filter:blur(8px);-webkit-backdrop-filter:blur(8px);
  border:1px solid rgba(127,127,127,.25);border-radius:999px;
  font:14px/1 system-ui,sans-serif;
}
#oc-theme-switcher button{
  appearance:none;border:0;background:transparent;color:inherit;
  width:32px;height:32px;border-radius:50%;cursor:pointer;
  display:inline-flex;align-items:center;justify-content:center;
  transition:background .15s ease,transform .1s ease;font-size:16px;
}
#oc-theme-switcher button:hover{background:rgba(127,127,127,.2)}
#oc-theme-switcher button:active{transform:scale(.92)}
#oc-theme-switcher button[aria-pressed="true"]{
  background:rgba(111,66,193,.25);box-shadow:inset 0 0 0 1px rgba(111,66,193,.55);
}
@media print{#oc-theme-switcher{display:none!important}}
</style>
<script>
(function(){
  var KEY='oc-theme';
  var root=document.documentElement;
  function apply(t){
    root.classList.remove('theme-light','theme-dark');
    if(t==='light')root.classList.add('theme-light');
    else if(t==='dark')root.classList.add('theme-dark');
    var btns=document.querySelectorAll('#oc-theme-switcher button');
    for(var i=0;i<btns.length;i++){
      btns[i].setAttribute('aria-pressed', btns[i].getAttribute('data-theme')===t ? 'true' : 'false');
    }
  }
  function init(){
    var saved=null;
    try{saved=localStorage.getItem(KEY);}catch(e){}
    apply(saved==='light'||saved==='dark' ? saved : 'system');
    var btns=document.querySelectorAll('#oc-theme-switcher button');
    for(var i=0;i<btns.length;i++){
      btns[i].addEventListener('click',function(){
        var t=this.getAttribute('data-theme');
        try{
          if(t==='system')localStorage.removeItem(KEY);
          else localStorage.setItem(KEY,t);
        }catch(e){}
        apply(t);
      });
    }
  }
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',init);
  else init();
})();
</script>
'@

function Inject-Switcher {
  param([string]$HtmlPath)
  $html = Get-Content $HtmlPath -Raw
  if ($html -match 'id="oc-theme-switcher"') { return $false }   # already injected
  $marker = '</body>'
  if ($html -notmatch [regex]::Escape($marker)) {
    Write-Warning "No </body> in $HtmlPath; appending"
    $html = $html + "`n" + $switcherHtml
  } else {
    $html = $html -replace [regex]::Escape($marker), ($switcherHtml + "`n" + $marker)
  }
  Set-Content -Path $HtmlPath -Value $html -NoNewline
  return $true
}

# Discover sessions
if (-not $Sessions -or $Sessions.Count -eq 0) {
  $Sessions = Get-ChildItem -Path $Root -Directory `
    | Where-Object { $_.Name -like 'session-*' -and (Test-Path (Join-Path $_.FullName 'slides.md')) } `
    | Select-Object -ExpandProperty Name
}

foreach ($s in $Sessions) {
  foreach ($variant in $Variants) {
    $md   = Join-Path $Root "$s/$variant.md"
    $html = Join-Path $Root "$s/$variant.html"
    if (-not (Test-Path $md)) {
      Write-Host "  · Skip $s/$variant — no $variant.md"
      continue
    }
    Write-Host "▶ Rendering $md"
    & npx --yes "@marp-team/marp-cli@latest" --html --allow-local-files --theme $ThemeCss $md -o $html
    if ($LASTEXITCODE -ne 0) { throw "marp failed for $s/$variant (exit $LASTEXITCODE)" }
    if (Inject-Switcher -HtmlPath $html) {
      Write-Host "  ✔ Theme switcher injected → $html"
    } else {
      Write-Host "  · Switcher already present → $html"
    }
  }
}

Write-Host "`nDone. Open the HTML files locally or push to GitHub Pages."
