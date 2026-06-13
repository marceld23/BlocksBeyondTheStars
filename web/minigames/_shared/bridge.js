/*
 * Shared bridge between a bundled minigame (HTML/JS) and the SpaceCraft client (C#).
 *
 * The C# EmbeddedBrowser host loads each game with query params:
 *   ?lang=de|en   active UI language (in-game text must be bilingual)
 *   ?hi=<int>     the player's local best score for this game (from ClientSettings)
 *
 * A game reports a score back to C# (persisted as the local personal best) by calling
 * BBS.reportScore(score). Under UnityWebBrowser the `uwb.ExecuteJsMethod` bridge is present;
 * in a plain dev browser it is absent, so the call is a safe no-op (the game still runs for
 * testing). Highscores are LOCAL and personal — there is no server leaderboard.
 */
window.BBS = (function () {
  var params = new URLSearchParams(location.search);
  var lang = params.get('lang') === 'de' ? 'de' : 'en';
  var best = parseInt(params.get('hi') || '0', 10);
  if (isNaN(best) || best < 0) best = 0;
  var gameKey = params.get('game') || (location.pathname.split('/').filter(Boolean).slice(-2)[0] || '');

  function reportScore(score) {
    var s = Math.max(0, Math.round(Number(score) || 0));
    try {
      if (typeof uwb !== 'undefined' && uwb && typeof uwb.ExecuteJsMethod === 'function') {
        uwb.ExecuteJsMethod('reportScore', gameKey, s);
      }
    } catch (e) { /* dev browser without the bridge — ignore */ }
  }

  // Pick the active-language string from a { en, de } map.
  function t(map) {
    if (map == null) return '';
    return map[lang] != null ? map[lang] : (map.en != null ? map.en : '');
  }

  return { lang: lang, best: best, gameKey: gameKey, reportScore: reportScore, t: t };
})();
