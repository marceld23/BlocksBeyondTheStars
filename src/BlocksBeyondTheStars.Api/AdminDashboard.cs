// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Api;

/// <summary>The single-page admin dashboard, served at the site root. Tooling UI is in English.</summary>
public static class AdminDashboard
{
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Blocks Beyond the Stars — Server Admin</title>
  <style>
    body { font-family: system-ui, sans-serif; margin: 0; background: #0b0f1a; color: #dfe6f3; }
    header { background: #131a2b; padding: 16px 24px; border-bottom: 1px solid #243049; }
    h1 { margin: 0; font-size: 20px; }
    main { padding: 24px; max-width: 900px; margin: 0 auto; display: grid; gap: 20px; }
    .card { background: #131a2b; border: 1px solid #243049; border-radius: 8px; padding: 16px; }
    .card h2 { margin-top: 0; font-size: 16px; color: #8fb4ff; }
    button { background: #2b6cff; color: #fff; border: 0; border-radius: 6px; padding: 8px 14px; cursor: pointer; }
    button:hover { background: #1f5be0; }
    input, textarea { background: #0b0f1a; color: #dfe6f3; border: 1px solid #243049; border-radius: 6px; padding: 8px; width: 100%; box-sizing: border-box; }
    textarea { min-height: 320px; font-family: ui-monospace, monospace; font-size: 12px; }
    pre { background: #0b0f1a; border: 1px solid #243049; border-radius: 6px; padding: 12px; overflow: auto; max-height: 320px; font-size: 12px; }
    .row { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
    .warn { color: #ffcc66; }
    table { width: 100%; border-collapse: collapse; }
    td, th { text-align: left; padding: 6px 8px; border-bottom: 1px solid #243049; font-size: 13px; }
    .muted { color: #8a96ad; font-size: 12px; }
  </style>
</head>
<body>
  <header><h1>🚀 Blocks Beyond the Stars — Server Admin</h1></header>
  <main>
    <div class="card">
      <div class="row">
        <label for="pw">Admin password</label>
        <input id="pw" type="password" style="max-width: 260px" placeholder="if configured" />
        <button onclick="savePw()">Use</button>
        <span id="authmsg" class="muted"></span>
      </div>
    </div>

    <div class="card">
      <h2>Status <button style="float:right" onclick="loadStatus()">Refresh</button></h2>
      <div id="status">Loading…</div>
    </div>

    <div class="card">
      <h2>Backups
        <button style="float:right" onclick="createBackup()">Create backup</button>
      </h2>
      <div id="backups">Loading…</div>
    </div>

    <div class="card">
      <h2>Configuration <button style="float:right" onclick="saveConfig()">Save</button></h2>
      <p class="muted">Edits take effect on the next server start.</p>
      <textarea id="config"></textarea>
    </div>

    <div class="card">
      <h2>Logs <button style="float:right" onclick="loadLogs()">Refresh</button></h2>
      <pre id="logs">Loading…</pre>
    </div>
  </main>

  <script>
    let pw = localStorage.getItem('scAdminPw') || '';
    document.getElementById('pw').value = pw;
    function headers() { return pw ? { 'X-Admin-Password': pw, 'Content-Type': 'application/json' } : { 'Content-Type': 'application/json' }; }
    function savePw() { pw = document.getElementById('pw').value; localStorage.setItem('scAdminPw', pw); document.getElementById('authmsg').textContent = 'saved'; loadAll(); }

    async function api(path, opts) {
      const res = await fetch(path, Object.assign({ headers: headers() }, opts || {}));
      if (res.status === 401) { document.getElementById('authmsg').textContent = 'unauthorized — set the admin password'; throw new Error('401'); }
      return res;
    }

    async function loadStatus() {
      try {
        const s = await (await api('/api/status')).json();
        let html = '<table>';
        const rows = [['Server', s.serverName], ['World', s.worldName], ['Gameplay port', s.gameplayPort],
          ['Admin port', s.adminPort], ['Max players', s.maxPlayers], ['World exists', s.worldExists],
          ['World size', (s.worldSizeBytes/1024).toFixed(1) + ' KiB'], ['Last modified', s.lastModifiedUtc || '—'],
          ['Registered players', s.registeredPlayers], ['Backups', s.backupCount], ['Admin password set', s.adminPasswordSet]];
        for (const [k,v] of rows) html += `<tr><th>${k}</th><td>${v}</td></tr>`;
        html += '</table>';
        if (s.warning) html += `<p class="warn">⚠ ${s.warning}</p>`;
        document.getElementById('status').innerHTML = html;
      } catch (e) { document.getElementById('status').textContent = 'Error: ' + e.message; }
    }

    async function loadConfig() {
      try { const c = await (await api('/api/config')).json(); document.getElementById('config').value = JSON.stringify(c, null, 2); }
      catch (e) { document.getElementById('config').value = 'Error: ' + e.message; }
    }
    async function saveConfig() {
      try { await api('/api/config', { method: 'PUT', body: document.getElementById('config').value }); alert('Configuration saved.'); loadStatus(); }
      catch (e) { alert('Save failed: ' + e.message); }
    }

    async function loadBackups() {
      try {
        const list = await (await api('/api/backups')).json();
        let html = '<table><tr><th>Name</th><th>Size</th><th>Modified (UTC)</th></tr>';
        for (const b of list) html += `<tr><td>${b.name}</td><td>${(b.sizeBytes/1024).toFixed(1)} KiB</td><td>${b.modifiedUtc}</td></tr>`;
        html += '</table>';
        document.getElementById('backups').innerHTML = list.length ? html : '<span class="muted">No backups yet.</span>';
      } catch (e) { document.getElementById('backups').textContent = 'Error: ' + e.message; }
    }
    async function createBackup() {
      try { const r = await (await api('/api/backups', { method: 'POST' })).json(); alert('Created: ' + r.name); loadBackups(); loadStatus(); }
      catch (e) { alert('Backup failed: ' + e.message); }
    }

    async function loadLogs() {
      try { const list = await (await api('/api/logs?lines=200')).json(); document.getElementById('logs').textContent = list.join('\n') || '(empty)'; }
      catch (e) { document.getElementById('logs').textContent = 'Error: ' + e.message; }
    }

    function loadAll() { loadStatus(); loadConfig(); loadBackups(); loadLogs(); }
    loadAll();
  </script>
</body>
</html>
""";
}
