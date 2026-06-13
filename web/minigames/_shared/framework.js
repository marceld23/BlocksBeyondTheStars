/*
 * Shared minigame framework for the DataQubes data-cube minigames.
 *
 * Provides the unified shell every game uses — title/HUD bar, a #bbts-stage, and the start / help / pause /
 * result overlays — plus an abstract input model (Confirm/Cancel/Pause/Restart/Help/Primary/Secondary +
 * Up/Down/Left/Right), a paused-aware rAF loop, a stage pointer helper, and the Unity bridge. A game only
 * implements its mechanic and calls api.complete()/api.fail(); the frame, controls and reward are uniform.
 *
 * A game registers like:
 *   BBTS.register({
 *     title: {en:"…", de:"…"}, desc:{en,de}, help:[{en,de}, …], difficulty: 2,
 *     create(api) { … return { start(), restart?(), pause?(), resume?() }; }
 *   });
 *
 * On completion the framework reports {score, rating 0-3, completed} to Unity, which grants knowledge points.
 */
(function () {
  var params = new URLSearchParams(location.search);
  var lang = params.get('lang') === 'de' ? 'de' : 'en';
  var best = parseInt(params.get('hi') || '0', 10); if (isNaN(best) || best < 0) best = 0;
  var gameKey = params.get('game') || (location.pathname.split('/').filter(Boolean).slice(-2)[0] || '');

  function t(m) { if (m == null) return ''; if (typeof m === 'string') return m; return m[lang] != null ? m[lang] : (m.en || ''); }
  var STR = {
    start: { en: 'Start', de: 'Starten' }, help: { en: 'Help', de: 'Hilfe' }, back: { en: 'Back', de: 'Zurück' },
    resume: { en: 'Resume', de: 'Fortsetzen' }, restart: { en: 'Restart', de: 'Neu starten' },
    quit: { en: 'Quit', de: 'Verlassen' }, again: { en: 'Play again', de: 'Erneut' }, close: { en: 'Close', de: 'Schließen' },
    paused: { en: 'Paused', de: 'Pausiert' }, done: { en: 'Complete', de: 'Abgeschlossen' }, failed: { en: 'Failed', de: 'Fehlgeschlagen' },
    score: { en: 'Score', de: 'Punkte' }, time: { en: 'Time', de: 'Zeit' }, best: { en: 'Best', de: 'Beste' },
    diff: { en: 'Difficulty', de: 'Schwierigkeit' }, goal: { en: 'Goal', de: 'Ziel' },
    knowledge: { en: 'knowledge', de: 'Wissen' }, controls: { en: 'Controls', de: 'Steuerung' }
  };

  function reportResult(score, rating, completed) {
    var s = Math.max(0, Math.round(score || 0)), r = Math.max(0, Math.min(3, rating | 0));
    try {
      if (typeof uwb !== 'undefined' && uwb && typeof uwb.ExecuteJsMethod === 'function') {
        uwb.ExecuteJsMethod('reportResult', gameKey, s, r, !!completed);
      }
    } catch (e) { /* dev browser without the bridge */ }
  }

  function el(tag, cls, parent, html) {
    var e = document.createElement(tag); if (cls) e.className = cls; if (html != null) e.innerHTML = html;
    if (parent) parent.appendChild(e); return e;
  }
  function btn(label, parent, ghost) { var b = el('button', 'bbts-btn' + (ghost ? ' ghost' : ''), parent, label); return b; }

  // Abstract input mapping (keyboard). Pointer is handled per-game via api.pointer().
  var KEYMAP = {
    arrowleft: 'Left', a: 'Left', arrowright: 'Right', d: 'Right',
    arrowup: 'Up', w: 'Up', arrowdown: 'Down', s: 'Down',
    enter: 'Confirm', ' ': 'Primary', shift: 'Secondary',
    escape: 'Pause', p: 'Pause', r: 'Restart', h: 'Help'
  };

  var BBTS = {
    lang: lang, best: best, gameKey: gameKey, t: t, reportResult: reportResult,
    register: function (def) { document.addEventListener('DOMContentLoaded', function () { boot(def); }); }
  };
  window.BBTS = BBTS;

  function boot(def) {
    document.title = t(def.title);
    var app = el('div', null, document.body); app.id = 'bbts-app';
    var bar = el('div', null, app); bar.id = 'bbts-bar';
    el('div', null, bar, t(def.title)).id = 'bbts-title';
    var hud = el('div', null, bar); hud.id = 'bbts-hud';
    var stage = el('div', null, app); stage.id = 'bbts-stage';
    var hint = el('div', null, app, t(STR.controls) + ': ' + (def.hint ? t(def.hint) : '')); hint.id = 'bbts-hint';

    // --- overlays ---
    function overlay() { return el('div', 'bbts-overlay', stage); }
    var oStart = overlay(), oHelp = overlay(), oPause = overlay(), oResult = overlay();

    // Start
    el('h2', null, oStart, t(def.title));
    var diff = el('div', 'bbts-diff', oStart);
    for (var i = 0; i < 5; i++) el('i', i < (def.difficulty || 1) ? 'on' : '', diff);
    if (def.desc) el('p', null, oStart, t(def.desc));
    var sRow = el('div', 'bbts-row', oStart);
    btn(t(STR.start), sRow).onclick = function () { startGame(); };
    btn(t(STR.help), sRow, true).onclick = function () { show(oHelp); };

    // Help
    el('h2', null, oHelp, t(STR.help));
    if (def.desc) { el('div', 'sub', oHelp, t(STR.goal)); el('p', null, oHelp, t(def.desc)); }
    var ul = el('ul', 'bbts-help', oHelp);
    (def.help || []).forEach(function (h) { el('li', null, ul, t(h)); });
    btn(t(STR.back), oHelp, true).onclick = function () { show(prevOverlay || oStart); };

    // Pause
    el('h2', null, oPause, t(STR.paused));
    var pRow = el('div', 'bbts-row', oPause);
    btn(t(STR.resume), pRow).onclick = function () { resume(); };
    btn(t(STR.restart), pRow, true).onclick = function () { startGame(); };
    btn(t(STR.help), pRow, true).onclick = function () { prevOverlay = oPause; show(oHelp); };
    btn(t(STR.quit), pRow, true).onclick = function () { show(oStart); paused = false; };

    // Result (filled on complete)
    var rTitle = el('h2', null, oResult), rStars = el('div', 'bbts-stars', oResult);
    for (var k = 0; k < 3; k++) el('span', null, rStars, '◆');
    var rScore = el('p', null, oResult), rTime = el('p', null, oResult), rReward = el('div', 'bbts-reward', oResult);
    var rRow = el('div', 'bbts-row', oResult);
    btn(t(STR.again), rRow).onclick = function () { startGame(); };
    btn(t(STR.close), rRow, true).onclick = function () { show(oStart); };

    var prevOverlay = oStart;
    function show(o) { [oStart, oHelp, oPause, oResult].forEach(function (x) { x.classList.toggle('show', x === o); }); }
    show(oStart);

    // --- state ---
    var held = {}, pressHandlers = {}, loopFns = [], pointerCb = null;
    var running = false, paused = false, startT = 0, pausedAccum = 0, pauseStart = 0, lastFrame = 0, hudData = {};
    var controller = null;

    var api = {
      lang: lang, t: t, best: best, stage: stage,
      el: el,
      canvas: function (w, h) {
        var c = el('canvas', null, stage); c.width = w; c.height = h; api._canvas = c; api.ctx = c.getContext('2d'); return c;
      },
      hud: function (obj) { for (var key in obj) hudData[key] = obj[key]; renderHud(); },
      bind: function (action, fn) { (pressHandlers[action] = pressHandlers[action] || []).push(fn); },
      held: function (action) { return !!held[action]; },
      pointer: function (cb) { pointerCb = cb; },
      loop: function (fn) { loopFns.push(fn); },
      now: function () { return running ? (perf() - startT - pausedAccum) / 1000 : 0; },
      rand: function (n) { return Math.floor(Math.random() * n); },
      shuffle: function (arr) { for (var i = arr.length - 1; i > 0; i--) { var j = Math.floor(Math.random() * (i + 1)); var tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp; } return arr; },
      complete: function (res) { finish(res || {}, true); },
      fail: function (res) { finish(res || {}, false); }
    };

    function perf() { return (window.performance && performance.now) ? performance.now() : new Date().getTime(); }
    var _lastHud = '';
    function renderHud() {
      var parts = [];
      if ('score' in hudData) parts.push('<span>' + t(STR.score) + '<b>' + hudData.score + '</b></span>');
      for (var key in hudData) { if (key === 'score' || key === 'time') continue; parts.push('<span>' + key + '<b>' + hudData[key] + '</b></span>'); }
      parts.push('<span>' + t(STR.time) + '<b>' + fmtTime(api.now()) + '</b></span>');
      if (best) parts.push('<span>' + t(STR.best) + '<b>' + best + '</b></span>');
      var html = parts.join('');
      if (html !== _lastHud) { _lastHud = html; hud.innerHTML = html; } // only reflow when something changed (time ticks once/sec)
    }
    function fmtTime(s) { s = Math.max(0, Math.floor(s)); var m = Math.floor(s / 60); var ss = s % 60; return m + ':' + (ss < 10 ? '0' : '') + ss; }

    function startGame() {
      // reset stage children except overlays
      Array.prototype.slice.call(stage.children).forEach(function (ch) { if (ch.className !== 'bbts-overlay') stage.removeChild(ch); });
      held = {}; pressHandlers = {}; loopFns = []; pointerCb = null; hudData = {}; api._canvas = null; api.ctx = null;
      show(null);
      controller = def.create(api);
      running = true; paused = false; startT = perf(); pausedAccum = 0; lastFrame = perf();
      if (controller && controller.start) controller.start();
      renderHud();
    }
    function pause() { if (!running || paused) return; paused = true; pauseStart = perf(); prevOverlay = oPause; show(oPause); if (controller && controller.pause) controller.pause(); }
    function resume() { if (!paused) return; paused = false; pausedAccum += perf() - pauseStart; lastFrame = perf(); show(null); if (controller && controller.resume) controller.resume(); }
    function finish(res, completed) {
      running = false; paused = false;
      var rating = completed ? Math.max(1, Math.min(3, res.rating != null ? res.rating : 1)) : 0;
      var score = Math.round(res.score || hudData.score || 0);
      rTitle.textContent = completed ? t(STR.done) : t(STR.failed);
      Array.prototype.forEach.call(rStars.children, function (st, i) { st.className = i < rating ? 'on' : ''; });
      rScore.innerHTML = t(STR.score) + ': <b style="color:var(--cyan)">' + score + '</b>';
      rTime.textContent = t(STR.time) + ': ' + fmtTime(api.now());
      var reward = completed ? rating * 5 : 0;
      rReward.textContent = reward > 0 ? ('+' + reward + ' ' + t(STR.knowledge)) : '';
      show(oResult);
      reportResult(score, rating, completed);
    }

    // input
    window.addEventListener('keydown', function (e) {
      var a = KEYMAP[e.key.toLowerCase()];
      if (!a) return;
      e.preventDefault();
      if (a === 'Pause') { if (paused) resume(); else if (running) pause(); return; }
      if (paused) return;
      if (a === 'Restart') { startGame(); return; }
      if (a === 'Help') { prevOverlay = running ? oPause : oStart; show(oHelp); if (running) pause(); return; }
      if (!held[a]) { held[a] = true; (pressHandlers[a] || []).forEach(function (fn) { fn(); }); }
      if (a === 'Confirm') (pressHandlers.Confirm || []).forEach(function () {}); // press already fired
    });
    window.addEventListener('keyup', function (e) { var a = KEYMAP[e.key.toLowerCase()]; if (a) held[a] = false; });

    // pointer on stage (coords relative to the canvas if present, else stage)
    function ptr(e, type) {
      if (!pointerCb || paused || !running) return;
      var ref = api._canvas || stage; var rect = ref.getBoundingClientRect();
      var sx = api._canvas ? api._canvas.width / rect.width : 1, sy = api._canvas ? api._canvas.height / rect.height : 1;
      var x = (e.clientX - rect.left) * sx, y = (e.clientY - rect.top) * sy;
      pointerCb({ type: type, x: x, y: y, raw: e });
    }
    stage.addEventListener('mousedown', function (e) { ptr(e, 'down'); });
    stage.addEventListener('mousemove', function (e) { ptr(e, 'move'); });
    window.addEventListener('mouseup', function (e) { ptr(e, 'up'); });

    // main loop
    function frame() {
      var n = perf(); var dt = Math.min(0.05, (n - lastFrame) / 1000); lastFrame = n;
      if (running && !paused) { for (var i = 0; i < loopFns.length; i++) loopFns[i](dt); }
      if (running && !paused) renderHud();
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  }
})();
