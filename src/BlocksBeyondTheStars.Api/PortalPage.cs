namespace BlocksBeyondTheStars.Api;

/// <summary>
/// The public server portal landing page (anf_webclient.md §7): a polished page carrying the
/// <b>JuMaVe Games</b> studio logo and the <b>Blocks Beyond the Stars</b> game logo, a one-click
/// Windows client download (the Velopack <c>Setup.exe</c> served at <c>/download</c>) and the in-app
/// update-feed URL players paste into the game.
///
/// Both logos are recreated as inline SVG/CSS so the page is fully self-contained — it needs no shipped
/// art and renders on an offline LAN server. The studio emblem mirrors the in-game
/// <c>StudioSplash</c>: an iso block-cluster inside a sweeping cyan orbit ring with a rocket, the
/// tri-colour wordmark (Ju cyan · Ma white · Ve orange) and the slogan "Built from imagination.".
/// </summary>
public static class PortalPage
{
    public static string Render(string serverName, string worldName, int gameplayPort, string baseUrl)
    {
        return Template
            .Replace("__SERVER__", System.Net.WebUtility.HtmlEncode(serverName ?? string.Empty))
            .Replace("__WORLD__", System.Net.WebUtility.HtmlEncode(worldName ?? string.Empty))
            .Replace("__PORT__", gameplayPort.ToString())
            .Replace("__BASEURL__", System.Net.WebUtility.HtmlEncode(baseUrl ?? string.Empty));
    }

    // Single-quoted attributes throughout so this stays a plain verbatim string (no escaping needed).
    private const string Template = @"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>__SERVER__ — Blocks Beyond the Stars</title>
<link rel='preconnect' href='https://fonts.googleapis.com'>
<link href='https://fonts.googleapis.com/css2?family=Rajdhani:wght@500;600;700&display=swap' rel='stylesheet'>
<style>
:root{--cyan:#5fd7ff;--cyandim:#2b9fd6;--orange:#ff8c26;--line:#21304c}
*{box-sizing:border-box}
html,body{margin:0;min-height:100%}
body{font-family:'Rajdhani',system-ui,'Segoe UI',sans-serif;color:#dfe9f7;
 background:radial-gradient(1100px 640px at 50% -12%,#15243f 0%,#070a12 58%),#070a12;
 display:flex;align-items:center;justify-content:center;padding:34px;overflow-x:hidden}
.sky{position:fixed;inset:0;pointer-events:none;
 background-image:radial-gradient(1.4px 1.4px at 20% 30%,#bfe7ff88,transparent),
  radial-gradient(1.2px 1.2px at 75% 22%,#ffffff66,transparent),
  radial-gradient(1.6px 1.6px at 60% 70%,#bfe7ff55,transparent),
  radial-gradient(1.2px 1.2px at 35% 80%,#ffffff44,transparent),
  radial-gradient(1.3px 1.3px at 88% 62%,#bfe7ff66,transparent)}
.card{position:relative;width:min(560px,100%);background:linear-gradient(180deg,#0f1a2e,#0a1019);
 border:1px solid var(--line);border-radius:20px;padding:36px 36px 26px;
 box-shadow:0 30px 90px rgba(0,0,0,.65),inset 0 0 0 1px rgba(95,215,255,.04)}
.studio{display:flex;flex-direction:column;align-items:center}
.emblem{width:230px;height:165px;display:block}
.orbit{transform-box:view-box;transform-origin:120px 84px;animation:spin 16s linear infinite}
@keyframes spin{to{transform:rotate(360deg)}}
.tw{animation:tw 2.6s ease-in-out infinite}
.tw2{animation:tw 3.3s ease-in-out infinite .6s}
.tw3{animation:tw 2.1s ease-in-out infinite 1.1s}
@keyframes tw{0%,100%{opacity:.25}50%{opacity:1}}
.wordmark{font-size:42px;font-weight:700;letter-spacing:.5px;line-height:1}
.ju{color:var(--cyan)}.ma{color:#eef4ff}.ve{color:var(--orange)}
.games{letter-spacing:.52em;font-size:13px;font-weight:600;color:var(--cyandim);margin:6px 0 0;padding-left:.52em}
.slogan{font-size:11px;color:#8aa0bd;letter-spacing:.2em;text-transform:uppercase;margin-top:7px}
.rule{height:1px;background:linear-gradient(90deg,transparent,var(--line),transparent);margin:22px 0 0}
.logo{font-size:31px;font-weight:700;text-align:center;letter-spacing:.15em;text-transform:uppercase;
 margin:20px 0 2px;color:#eaf6ff;text-shadow:0 0 20px rgba(95,215,255,.5),0 0 3px rgba(95,215,255,.85)}
.logo b{color:var(--cyan);font-weight:700}
.meta{text-align:center;color:#8aa0bd;font-size:13px;margin:6px 0 22px;letter-spacing:.03em}
a.btn{display:flex;align-items:center;justify-content:center;gap:9px;text-decoration:none;
 padding:14px;border-radius:12px;font-weight:700;font-size:16px;letter-spacing:.03em;margin:10px 0;transition:.15s}
a.primary{background:linear-gradient(180deg,#2f8fff,#1d68d8);color:#fff;box-shadow:0 10px 26px rgba(29,104,216,.45)}
a.primary:hover{filter:brightness(1.08);transform:translateY(-1px)}
a.ghost{background:rgba(95,215,255,.05);border:1px solid var(--line);color:var(--cyan);font-size:15px}
a.ghost:hover{background:rgba(95,215,255,.1)}
.hint{margin-top:18px;padding:13px 15px;border:1px dashed var(--line);border-radius:11px;font-size:12.5px;color:#9fb3cf;line-height:1.5}
.hint code{color:var(--cyan);background:rgba(95,215,255,.09);padding:2px 7px;border-radius:6px;font-size:12px}
.foot{text-align:center;color:#566481;font-size:11px;margin-top:20px;letter-spacing:.1em;text-transform:uppercase}
</style>
</head>
<body>
<div class='sky'></div>
<div class='card'>
 <div class='studio'>
  <svg class='emblem' viewBox='0 0 240 165' xmlns='http://www.w3.org/2000/svg'>
   <defs>
    <radialGradient id='glow' cx='50%' cy='50%' r='50%'>
     <stop offset='0%' stop-color='#5fd7ff' stop-opacity='.5'/>
     <stop offset='100%' stop-color='#5fd7ff' stop-opacity='0'/>
    </radialGradient>
   </defs>
   <circle cx='120' cy='84' r='66' fill='url(#glow)'/>
   <circle class='tw'  cx='42'  cy='40'  r='2.4' fill='#bfe7ff'/>
   <circle class='tw2' cx='205' cy='50'  r='2'   fill='#ffffff'/>
   <circle class='tw3' cx='192' cy='120' r='2.4' fill='#bfe7ff'/>
   <circle class='tw2' cx='52'  cy='122' r='1.8' fill='#ffffff'/>
   <circle class='tw'  cx='150' cy='20'  r='1.8' fill='#bfe7ff'/>
   <g>
    <g><rect x='104' y='64' width='24' height='24' rx='3' fill='#2c7193'/><rect x='104' y='64' width='24' height='8' rx='3' fill='#46a4c6'/></g>
    <g><rect x='124' y='68' width='24' height='24' rx='3' fill='#367e9f'/><rect x='124' y='68' width='24' height='8' rx='3' fill='#5cb6d6'/></g>
    <g><rect x='92'  y='82' width='24' height='24' rx='3' fill='#57c2e4'/><rect x='92'  y='82' width='24' height='8' rx='3' fill='#8fe2ff'/></g>
    <g><rect x='112' y='88' width='24' height='24' rx='3' fill='#3f97bb'/><rect x='112' y='88' width='24' height='8' rx='3' fill='#67c4e2'/></g>
    <g><rect x='132' y='84' width='24' height='24' rx='3' fill='#2f86aa'/><rect x='132' y='84' width='24' height='8' rx='3' fill='#52aecf'/></g>
   </g>
   <g class='orbit'>
    <ellipse cx='120' cy='84' rx='98' ry='50' fill='none' stroke='#5fd7ff' stroke-opacity='.65' stroke-width='2'/>
    <g transform='translate(218,84)'>
     <polygon points='0,-8 5.5,7 -5.5,7' fill='#f4fbff'/>
     <circle cx='0' cy='10' r='3.4' fill='#ff8c26'/>
    </g>
   </g>
  </svg>
  <div class='wordmark'><span class='ju'>Ju</span><span class='ma'>Ma</span><span class='ve'>Ve</span></div>
  <div class='games'>G A M E S</div>
  <div class='slogan'>Built from imagination.</div>
 </div>
 <div class='rule'></div>
 <div class='logo'><b>Blocks</b> Beyond the Stars</div>
 <div class='meta'>Server “__SERVER__” · World “__WORLD__” · native clients join on UDP __PORT__</div>
 <a class='btn primary' href='/download'>⬇&nbsp; Download the Windows client</a>
 <a class='btn ghost' href='/play'>▶&nbsp; Play in the browser</a>
 <div class='hint'>Already installed? Updates come straight from this server — paste
  <code>__BASEURL__/updates</code> into <b>Settings → Software update</b> in the game.</div>
 <div class='foot'>JuMaVe Games · __SERVER__</div>
</div>
</body>
</html>";
}
