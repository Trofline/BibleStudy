using System.Text.Json;

namespace BibleStudy.Core.Live;

/// <summary>
/// The web page the phones open: what is shown now (large and calm), the handout, a question to the leader and the poll.
/// One file without anything from outside (it works in a network without internet); light or dark as the phone is set.
/// The texts come in from the program, in its language.
/// </summary>
public static class LivePage
{
    /// <param name="texts">now, handout, ask, poll, waiting, askHint, name, send, sent, tooMany, vote, voted, results, noPoll, noHandout, offline, title</param>
    public static string Build(IReadOnlyDictionary<string, string> texts, string lang)
    {
        var t = JsonSerializer.Serialize(texts);
        string T(string key) => System.Net.WebUtility.HtmlEncode(texts.TryGetValue(key, out var v) ? v : key);
        var page = """
<!doctype html>
<html lang="@LANG@">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="light dark">
<title>@T:title@</title>
<style>
:root{--bg:#F7F3EA;--card:#FFFDF8;--text:#23201A;--muted:#6E665A;--accent:#9A7432;--line:#E4DCCB;--bar:#FFFDF8;--onaccent:#fff}
@media (prefers-color-scheme:dark){:root{--bg:#121110;--card:#1C1A17;--text:#F3EEE4;--muted:#A79F92;--accent:#C9A86A;--line:#2E2A24;--bar:#1A1815;--onaccent:#1A1611}}
*{box-sizing:border-box}
html,body{margin:0;background:var(--bg);color:var(--text);font:17px/1.55 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;-webkit-text-size-adjust:100%}
main{max-width:720px;margin:0 auto;padding:20px 16px calc(96px + env(safe-area-inset-bottom))}
section{display:none}section.on{display:block}
h1{font:600 15px/1.3 system-ui,sans-serif;letter-spacing:.08em;text-transform:uppercase;color:var(--accent);margin:4px 0 14px}
.ref{font:600 20px/1.3 Georgia,"Times New Roman",serif;color:var(--accent);margin:0 0 12px}
.verse{font:21px/1.7 Georgia,"Times New Roman",serif;white-space:pre-wrap;overflow-wrap:anywhere}
.muted{color:var(--muted)}
.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px;margin:0 0 14px}
textarea,input{width:100%;font:inherit;color:var(--text);background:var(--bg);border:1px solid var(--line);border-radius:10px;padding:12px;margin:0 0 10px}
textarea{min-height:120px;resize:vertical}
textarea:focus,input:focus{outline:2px solid var(--accent);outline-offset:1px}
button{font:600 17px system-ui,sans-serif;border:0;border-radius:12px;min-height:48px;padding:0 18px;cursor:pointer}
.primary{background:var(--accent);color:var(--onaccent);width:100%}
.opt{display:block;width:100%;text-align:left;background:var(--card);color:var(--text);border:1px solid var(--line);margin:0 0 10px;padding:12px 16px;position:relative;overflow:hidden}
.opt.mine{border:2px solid var(--accent)}
.opt .fill{position:absolute;left:0;top:0;bottom:0;background:var(--accent);opacity:.18}
.opt span{position:relative}
.opt .n{float:right;color:var(--muted)}
.msg{min-height:1.5em;margin:4px 0 0}
iframe{width:100%;height:calc(100vh - 170px);border:1px solid var(--line);border-radius:14px;background:#fff}
nav{position:fixed;left:0;right:0;bottom:0;display:flex;background:var(--bar);border-top:1px solid var(--line);padding:6px 6px calc(6px + env(safe-area-inset-bottom))}
nav button{flex:1;background:none;color:var(--muted);font-size:13px;min-height:52px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:2px;position:relative}
nav button.on{color:var(--accent)}
nav svg{width:22px;height:22px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round}
nav .dot{position:absolute;top:8px;right:calc(50% - 18px);width:9px;height:9px;border-radius:50%;background:var(--accent);display:none}
nav .dot.on{display:block}
.off{position:fixed;top:0;left:0;right:0;background:#8A2D1F;color:#fff;text-align:center;padding:6px;font-size:14px;display:none}
</style>
</head>
<body>
<div class="off" id="off">@T:offline@</div>
<main>
<section id="s-now" class="on"><h1>@T:now@</h1><div id="now"><p class="muted">@T:waiting@</p></div></section>
<section id="s-handout"><h1>@T:handout@</h1><div id="handout"><p class="muted">@T:noHandout@</p></div></section>
<section id="s-ask"><h1>@T:ask@</h1>
<div class="card"><p class="muted" style="margin-top:0">@T:askHint@</p>
<textarea id="q" maxlength="500" aria-label="@T:ask@"></textarea>
<input id="name" maxlength="40" placeholder="@T:name@" aria-label="@T:name@" autocomplete="nickname">
<button class="primary" id="send">@T:send@</button><p class="msg muted" id="askmsg" role="status"></p></div></section>
<section id="s-poll"><h1>@T:poll@</h1><div id="poll"><p class="muted">@T:noPoll@</p></div></section>
</main>
<nav>
<button class="on" data-s="now"><svg viewBox="0 0 24 24"><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20V2H6.5A2.5 2.5 0 0 0 4 4.5z"/></svg>@T:now@</button>
<button data-s="handout"><svg viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M8 13h8 M8 17h8"/></svg>@T:handout@<i class="dot" id="dh"></i></button>
<button data-s="ask"><svg viewBox="0 0 24 24"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>@T:ask@</button>
<button data-s="poll"><svg viewBox="0 0 24 24"><path d="M4 20V10 M12 20V4 M20 20v-6"/></svg>@T:poll@<i class="dot" id="dp"></i></button>
</nav>
<script>
const T=@JSON@;
const base=location.pathname.replace(/\/$/,'');
let voter;try{voter=localStorage.getItem('bs-voter')}catch(e){}
if(!voter){voter=Math.random().toString(36).slice(2)+Date.now().toString(36);try{localStorage.setItem('bs-voter',voter)}catch(e){}}
try{document.getElementById('name').value=localStorage.getItem('bs-name')||''}catch(e){}
let tab='now',last='',lastHandout=0,seenPoll=0;
function show(s){tab=s;document.querySelectorAll('section').forEach(x=>x.classList.toggle('on',x.id==='s-'+s));
document.querySelectorAll('nav button').forEach(b=>b.classList.toggle('on',b.dataset.s===s));
if(s==='poll')document.getElementById('dp').classList.remove('on');if(s==='handout')document.getElementById('dh').classList.remove('on');scrollTo(0,0)}
document.querySelectorAll('nav button').forEach(b=>b.onclick=()=>show(b.dataset.s));
function el(tag,cls,text){const e=document.createElement(tag);if(cls)e.className=cls;if(text!=null)e.textContent=text;return e}
function render(st){
const now=document.getElementById('now');now.replaceChildren();
if(st.title||st.text){if(st.title)now.append(el('p','ref',st.title));now.append(el('div','verse',st.text))}else now.append(el('p','muted',T.waiting));
if(st.handout!==lastHandout){lastHandout=st.handout;const h=document.getElementById('handout');h.replaceChildren();
if(st.handout){const f=document.createElement('iframe');f.src=base+'/handout?v='+st.handout;f.title=T.handout;h.append(f);if(tab!=='handout')document.getElementById('dh').classList.add('on')}else h.append(el('p','muted',T.noHandout))}
const p=document.getElementById('poll');p.replaceChildren();
if(!st.poll){p.append(el('p','muted',T.noPoll));return}
if(st.poll.id!==seenPoll){seenPoll=st.poll.id;if(tab!=='poll')document.getElementById('dp').classList.add('on')}
const card=el('div','card');card.append(el('p','ref',st.poll.question));
const total=st.poll.results?st.poll.results.reduce((a,b)=>a+b,0):0;
st.poll.options.forEach((o,i)=>{const b=el('button','opt'+(st.poll.voted===i?' mine':''));
if(st.poll.results){const f=el('i','fill');f.style.width=(total?Math.round(100*st.poll.results[i]/total):0)+'%';b.append(f);b.append(el('span','n',String(st.poll.results[i])))}
b.append(el('span',null,o));b.onclick=()=>vote(st.poll.id,i);card.append(b)});
card.append(el('p','msg muted',st.poll.voted!=null?T.voted:T.vote));p.append(card)}
async function load(){try{const r=await fetch(base+'/state?voter='+encodeURIComponent(voter),{cache:'no-store'});
if(!r.ok)throw 0;const s=await r.text();document.getElementById('off').style.display='none';if(s!==last){last=s;render(JSON.parse(s))}}
catch(e){document.getElementById('off').style.display='block'}}
async function tick(){await load();setTimeout(tick,1500)}
async function vote(poll,option){try{await fetch(base+'/vote',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({poll,option,voter})})}catch(e){}await load()}
document.getElementById('send').onclick=async()=>{const q=document.getElementById('q'),n=document.getElementById('name'),m=document.getElementById('askmsg');
if(!q.value.trim())return;try{localStorage.setItem('bs-name',n.value)}catch(e){}
try{const r=await fetch(base+'/ask',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({text:q.value,name:n.value})});
if(r.ok){q.value='';m.textContent=T.sent}else m.textContent=T.tooMany}catch(e){m.textContent=T.offline}};
tick();
</script>
</body>
</html>
""";
        page = System.Text.RegularExpressions.Regex.Replace(page, "@T:([a-zA-Z]+)@", m => T(m.Groups[1].Value));
        return page.Replace("@LANG@", System.Net.WebUtility.HtmlEncode(lang)).Replace("@JSON@", t);
    }
}
