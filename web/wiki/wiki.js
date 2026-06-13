/*
 * SpaceCraft Codex — in-game wiki SPA. Renders an always-on reference (tech, ships, blocks, items,
 * recipes, planet types) generated live from the game's content JSON, plus discovery-gated chapters
 * (Systems & Worlds) that only list places the player has actually visited. The discovered set + active
 * language come from wiki-state.json, which the client serves dynamically from live player state; in a
 * plain dev browser that file is absent, so those chapters fall back to "nothing discovered yet".
 *
 * All in-game text is bilingual (DE/EN): content names resolve through the same locale files the game uses.
 */
(async function () {
  var params = new URLSearchParams(location.search);
  var lang = params.get('lang') === 'de' ? 'de' : 'en';

  async function getJSON(url) {
    try { var r = await fetch(url, { cache: 'no-store' }); if (!r.ok) throw 0; return await r.json(); }
    catch (e) { return null; }
  }

  var state = await getJSON('wiki-state.json'); // { lang, systems:[], worlds:[] } — dynamic, may be null
  if (state && (state.lang === 'de' || state.lang === 'en')) lang = state.lang;
  document.documentElement.lang = lang;

  var enLoc = (await getJSON('../data/locales/en.json')) || {};
  var actLoc = lang === 'en' ? enLoc : ((await getJSON('../data/locales/' + lang + '.json')) || {});
  function L(key) { if (!key) return ''; return actLoc[key] || enLoc[key] || key; }
  function t(m) { return m && (m[lang] != null ? m[lang] : m.en) || ''; }

  var D = {
    articles: (await getJSON('articles.json')) || [],
    blocks: (await getJSON('../data/blocks.json')) || [],
    items: (await getJSON('../data/items.json')) || [],
    recipes: (await getJSON('../data/recipes.json')) || [],
    blueprints: (await getJSON('../data/blueprints.json')) || [],
    ships: (await getJSON('../data/ships.json')) || [],
    modules: (await getJSON('../data/ship_modules.json')) || [],
    planets: (await getJSON('../data/planets.json')) || []
  };
  var systems = (state && state.systems) || [];
  var worlds = (state && state.worlds) || [];

  // Lookups for cross-links.
  var itemByKey = {}; D.items.forEach(function (i) { itemByKey[i.key] = i; });
  var blockByKey = {}; D.blocks.forEach(function (b) { blockByKey[b.key] = b; });
  function itemName(key) { var i = itemByKey[key]; return i ? L(i.nameKey) : key; }

  function esc(s) { return String(s).replace(/[&<>]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]; }); }
  function link(go, label) { return '<span class="link" data-go="' + esc(go) + '">' + esc(label) + '</span>'; }
  function itemLink(key, count) {
    var label = itemName(key) + (count ? ' ×' + count : '');
    return itemByKey[key] ? link('item:' + key, label) : esc(label);
  }
  function costTable(list) {
    if (!list || !list.length) return '<p class="empty">' + t({ en: 'Free.', de: 'Kostenlos.' }) + '</p>';
    return '<table><tr><th>' + t({ en: 'Item', de: 'Gegenstand' }) + '</th><th>' + t({ en: 'Qty', de: 'Anz.' }) + '</th></tr>' +
      list.map(function (c) { return '<tr><td>' + itemLink(c.item) + '</td><td>' + (c.count || 1) + '</td></tr>'; }).join('') + '</table>';
  }

  // --- Category + entry registry ---
  var UI = {
    guide: { en: 'Guide', de: 'Anleitung' }, tech: { en: 'Tech', de: 'Technik' },
    ships: { en: 'Ships', de: 'Schiffe' }, modules: { en: 'Ship Modules', de: 'Schiffsmodule' },
    blocks: { en: 'Blocks', de: 'Blöcke' }, items: { en: 'Items', de: 'Gegenstände' },
    recipes: { en: 'Recipes', de: 'Rezepte' }, planets: { en: 'Planet Types', de: 'Planetentypen' },
    systems: { en: 'Systems', de: 'Systeme' }, worlds: { en: 'Worlds', de: 'Welten' }
  };
  var entries = {};   // id -> { cat, title, html() }
  var cats = [];      // { id, name, gated, items:[ids] }
  function addCat(id, gated) { var c = { id: id, name: t(UI[id]), gated: !!gated, items: [] }; cats.push(c); return c; }
  function add(cat, id, title, html) { entries[id] = { cat: cat.id, title: title, html: html }; cat.items.push(id); }

  // Guide (authored)
  var cGuide = addCat('guide');
  D.articles.forEach(function (a) {
    add(cGuide, 'guide:' + a.id, t(a.title), function () {
      return '<h1>' + esc(t(a.title)) + '</h1><div class="sub">' + esc(t(UI.guide)) + '</div>' + t(a.body);
    });
  });

  // Tech / blueprints
  var cTech = addCat('tech');
  D.blueprints.forEach(function (bp) {
    var title = L(bp.nameKey);
    add(cTech, 'bp:' + bp.key, title, function () {
      var prereq = (bp.prerequisites || []).map(function (k) {
        var p = D.blueprints.find(function (x) { return x.key === k; });
        return p ? link('bp:' + k, L(p.nameKey)) : esc(k);
      });
      return '<h1>' + esc(title) + '</h1><div class="sub">' + esc(t(UI.tech)) + ' · ' + esc(bp.category || '') + '</div>' +
        '<p>' + esc(L(bp.descriptionKey) || '') + '</p>' +
        '<h2>' + t({ en: 'Unlock cost', de: 'Freischaltkosten' }) + '</h2>' + costTable(bp.unlockCost) +
        (prereq.length ? '<h2>' + t({ en: 'Requires', de: 'Voraussetzung' }) + '</h2><p>' + prereq.join(', ') + '</p>' : '');
    });
  });

  // Ships
  var cShips = addCat('ships');
  D.ships.forEach(function (sh) {
    var title = L(sh.nameKey);
    add(cShips, 'ship:' + sh.key, title, function () {
      var mods = (sh.startModules || []).map(function (k) {
        return D.modules.find(function (m) { return m.key === k; }) ? link('mod:' + k, L('module.' + k + '.name')) : esc(k);
      });
      return '<h1>' + esc(title) + '</h1><div class="sub">' + esc(t(UI.ships)) + '</div>' +
        '<p>' + esc(L(sh.descriptionKey) || '') + '</p>' +
        '<table>' +
        '<tr><th>' + t({ en: 'Hull', de: 'Hülle' }) + '</th><td>' + (sh.baseHull || 0) + '</td></tr>' +
        '<tr><th>' + t({ en: 'Shield', de: 'Schild' }) + '</th><td>' + (sh.baseShield || 0) + '</td></tr>' +
        '<tr><th>' + t({ en: 'Speed', de: 'Tempo' }) + '</th><td>' + (sh.flightSpeed || 1) + '</td></tr>' +
        '<tr><th>' + t({ en: 'Handling', de: 'Handling' }) + '</th><td>' + (sh.handling || 1) + '</td></tr>' +
        '<tr><th>' + t({ en: 'Cargo slots', de: 'Frachtplätze' }) + '</th><td>' + (sh.cargoSlots || 0) + '</td></tr>' +
        '</table>' +
        (sh.craftCost ? '<h2>' + t({ en: 'Build cost', de: 'Baukosten' }) + '</h2>' + costTable(sh.craftCost) : '') +
        (mods.length ? '<h2>' + t({ en: 'Starting modules', de: 'Startmodule' }) + '</h2><p>' + mods.join(', ') + '</p>' : '');
    });
  });

  // Ship modules
  var cMods = addCat('modules');
  D.modules.forEach(function (m) {
    var title = L(m.nameKey);
    add(cMods, 'mod:' + m.key, title, function () {
      var stats = m.stats ? Object.keys(m.stats).map(function (k) {
        return '<tr><td>' + esc(k.replace(/_/g, ' ')) + '</td><td>' + m.stats[k] + '</td></tr>'; }).join('') : '';
      return '<h1>' + esc(title) + '</h1><div class="sub">' + esc(t(UI.modules)) + '</div>' +
        '<p>' + esc(L(m.descriptionKey) || '') + '</p>' +
        (m.mandatory ? '<div class="tags"><span class="tag good">' + t({ en: 'Core module', de: 'Kernmodul' }) + '</span></div>' : '') +
        (m.buildCost && m.buildCost.length ? '<h2>' + t({ en: 'Build cost', de: 'Baukosten' }) + '</h2>' + costTable(m.buildCost) : '') +
        (stats ? '<h2>' + t({ en: 'Stats', de: 'Werte' }) + '</h2><table>' + stats + '</table>' : '');
    });
  });

  // Blocks
  var cBlocks = addCat('blocks');
  D.blocks.forEach(function (b) {
    var title = L(b.nameKey);
    add(cBlocks, 'block:' + b.key, title, function () {
      var tool = (b.requiredTool && b.requiredTool !== 'none') ? esc(b.requiredTool) + ' T' + (b.minToolTier || 0)
        : t({ en: 'Bare hands', de: 'Bloße Hände' });
      var drops = (b.drops || []).map(function (d) { return itemLink(d.item, d.count); }).join(', ');
      return '<h1>' + esc(title) + '</h1><div class="sub">' + esc(t(UI.blocks)) + '</div>' +
        '<table>' +
        '<tr><th>' + t({ en: 'Hardness', de: 'Härte' }) + '</th><td>' + (b.hardness != null ? b.hardness : '—') + '</td></tr>' +
        '<tr><th>' + t({ en: 'Tool', de: 'Werkzeug' }) + '</th><td>' + tool + '</td></tr>' +
        '<tr><th>' + t({ en: 'Drops', de: 'Lässt fallen' }) + '</th><td>' + (drops || '—') + '</td></tr>' +
        '</table>';
    });
  });

  // Items
  var cItems = addCat('items');
  D.items.forEach(function (i) {
    var title = L(i.nameKey);
    add(cItems, 'item:' + i.key, title, function () {
      var rows = '<tr><th>' + t({ en: 'Category', de: 'Kategorie' }) + '</th><td>' + esc(i.category || '') + '</td></tr>' +
        '<tr><th>' + t({ en: 'Max stack', de: 'Max. Stapel' }) + '</th><td>' + (i.maxStack || 1) + '</td></tr>';
      if (i.placesBlock && blockByKey[i.placesBlock]) rows += '<tr><th>' + t({ en: 'Places', de: 'Platziert' }) + '</th><td>' + link('block:' + i.placesBlock, L(blockByKey[i.placesBlock].nameKey)) + '</td></tr>';
      if (i.tool) Object.keys(i.tool).forEach(function (k) { rows += '<tr><th>' + esc(k) + '</th><td>' + i.tool[k] + '</td></tr>'; });
      // recipes that make this item
      var made = D.recipes.filter(function (r) { return (r.outputs || []).some(function (o) { return o.item === i.key; }); });
      var madeHtml = made.length ? '<h2>' + t({ en: 'Crafted from', de: 'Hergestellt aus' }) + '</h2>' +
        made.map(function (r) { return '<p>' + (r.inputs || []).map(function (n) { return itemLink(n.item, n.count); }).join(' + ') + ' &rarr; @ ' + esc(r.station) + '</p>'; }).join('') : '';
      return '<h1>' + esc(title) + '</h1><div class="sub">' + esc(t(UI.items)) + '</div><table>' + rows + '</table>' + madeHtml;
    });
  });

  // Recipes (titled by primary output)
  var cRec = addCat('recipes');
  D.recipes.forEach(function (r, idx) {
    var out = (r.outputs && r.outputs[0]) ? r.outputs[0].item : r.key;
    var title = itemName(out);
    add(cRec, 'rec:' + idx, title, function () {
      return '<h1>' + esc(title) + '</h1><div class="sub">' + esc(t(UI.recipes)) + ' · ' + esc(r.station || '') + '</div>' +
        '<h2>' + t({ en: 'Inputs', de: 'Zutaten' }) + '</h2>' + costTable((r.inputs || []).map(function (n) { return { item: n.item, count: n.count }; })) +
        '<h2>' + t({ en: 'Outputs', de: 'Ergebnis' }) + '</h2>' + costTable((r.outputs || []).map(function (n) { return { item: n.item, count: n.count }; }));
    });
  });

  // Planet types (general knowledge)
  var cPlanets = addCat('planets');
  D.planets.forEach(function (p) {
    var title = L(p.nameKey);
    add(cPlanets, 'planet:' + p.key, title, function () {
      var ores = (p.ores || []).map(function (o) { return blockByKey[o.block] ? link('block:' + o.block, L(blockByKey[o.block].nameKey)) : esc(o.block); }).join(', ');
      return '<h1>' + esc(title) + '</h1><div class="sub">' + esc(t(UI.planets)) + '</div>' +
        '<table>' +
        '<tr><th>' + t({ en: 'Atmosphere', de: 'Atmosphäre' }) + '</th><td>' + esc(p.atmosphere || '—') + '</td></tr>' +
        '<tr><th>' + t({ en: 'Base temp', de: 'Grundtemp.' }) + '</th><td>' + (p.baseTemperature != null ? p.baseTemperature + '°' : '—') + '</td></tr>' +
        '<tr><th>' + t({ en: 'Surface', de: 'Oberfläche' }) + '</th><td>' + (blockByKey[p.surfaceBlock] ? link('block:' + p.surfaceBlock, L(blockByKey[p.surfaceBlock].nameKey)) : esc(p.surfaceBlock || '—')) + '</td></tr>' +
        '<tr><th>' + t({ en: 'Life', de: 'Leben' }) + '</th><td>' + esc(p.creatureAbundance || '—') + '</td></tr>' +
        '</table>' + (ores ? '<h2>' + t({ en: 'Notable ores', de: 'Erze' }) + '</h2><p>' + ores + '</p>' : '');
    });
  });

  // Systems (discovery-gated)
  var cSys = addCat('systems', true);
  systems.forEach(function (s) {
    add(cSys, 'sys:' + s.id, s.name, function () {
      var bodies = worlds.filter(function (w) { return w.systemId === s.id; });
      return '<h1>' + esc(s.name) + '</h1><div class="sub">' + esc(t(UI.systems)) + '</div>' +
        (s.starType ? '<p>' + t({ en: 'Star: ', de: 'Stern: ' }) + esc(s.starType) + '</p>' : '') +
        '<h2>' + t({ en: 'Known worlds', de: 'Bekannte Welten' }) + '</h2>' +
        (bodies.length ? '<p>' + bodies.map(function (w) { return link('world:' + w.id, w.name); }).join(', ') + '</p>'
          : '<p class="empty">' + t({ en: 'None visited here yet.', de: 'Hier noch nichts besucht.' }) + '</p>');
    });
  });

  // Worlds (discovery-gated)
  var cWorlds = addCat('worlds', true);
  worlds.forEach(function (w) {
    add(cWorlds, 'world:' + w.id, w.name, function () {
      var pt = w.type ? D.planets.find(function (p) { return p.key === w.type; }) : null;
      return '<h1>' + esc(w.name) + '</h1><div class="sub">' + esc(t(UI.worlds)) + '</div>' +
        '<table>' +
        (w.systemName ? '<tr><th>' + t({ en: 'System', de: 'System' }) + '</th><td>' + (w.systemId ? link('sys:' + w.systemId, w.systemName) : esc(w.systemName)) + '</td></tr>' : '') +
        (pt ? '<tr><th>' + t({ en: 'Type', de: 'Typ' }) + '</th><td>' + link('planet:' + pt.key, L(pt.nameKey)) + '</td></tr>' : (w.type ? '<tr><th>' + t({ en: 'Type', de: 'Typ' }) + '</th><td>' + esc(w.type) + '</td></tr>' : '')) +
        '</table>';
    });
  });

  // --- Rendering ---
  var navEl = document.getElementById('nav'), contentEl = document.getElementById('content'),
      searchEl = document.getElementById('search'), brandEl = document.getElementById('brand');
  brandEl.textContent = t({ en: 'CODEX', de: 'CODEX' });
  searchEl.placeholder = t({ en: 'Search…', de: 'Suchen…' });
  var active = null;

  function buildNav() {
    navEl.innerHTML = '';
    cats.forEach(function (c) {
      var wrap = document.createElement('div'); wrap.className = 'cat'; wrap.dataset.cat = c.id;
      var head = document.createElement('div'); head.className = 'catname';
      var count = c.gated ? c.items.length : c.items.length;
      head.innerHTML = '<span>' + esc(c.name) + '</span><span>' + count + '</span>';
      head.addEventListener('click', function () { wrap.classList.toggle('open'); showCat(c.id); });
      wrap.appendChild(head);
      var box = document.createElement('div'); box.className = 'items';
      if (c.items.length === 0 && c.gated) {
        var none = document.createElement('div'); none.className = 'navitem locked';
        none.textContent = t({ en: 'Nothing discovered', de: 'Nichts entdeckt' });
        box.appendChild(none);
      }
      c.items.forEach(function (id) {
        var it = document.createElement('div'); it.className = 'navitem'; it.dataset.id = id;
        it.textContent = entries[id].title;
        it.addEventListener('click', function () { showEntry(id); });
        box.appendChild(it);
      });
      wrap.appendChild(box);
      navEl.appendChild(wrap);
    });
  }
  function markActive(id) {
    active = id;
    Array.prototype.forEach.call(navEl.querySelectorAll('.navitem'), function (n) { n.classList.toggle('active', n.dataset.id === id); });
    var cat = id ? entries[id].cat : null;
    Array.prototype.forEach.call(navEl.querySelectorAll('.cat'), function (w) { if (w.dataset.cat === cat) w.classList.add('open'); });
  }
  function showEntry(id) {
    var e = entries[id]; if (!e) return;
    contentEl.innerHTML = e.html(); contentEl.scrollTop = 0; markActive(id);
  }
  function showCat(catId) {
    var c = cats.find(function (x) { return x.id === catId; }); if (!c) return;
    var cards = c.items.map(function (id) { return '<div class="card" data-go="' + esc(id) + '"><div class="n">' + esc(entries[id].title) + '</div></div>'; }).join('');
    contentEl.innerHTML = '<h1>' + esc(c.name) + '</h1><div class="sub">' + c.items.length + ' ' + t({ en: 'entries', de: 'Einträge' }) + '</div>' +
      (c.items.length ? '<div class="cards">' + cards + '</div>'
        : '<p class="empty">' + (c.gated ? t({ en: 'Nothing discovered yet — explore to fill this chapter.', de: 'Noch nichts entdeckt — erkunde, um dieses Kapitel zu füllen.' }) : t({ en: 'No entries.', de: 'Keine Einträge.' })) + '</p>');
    contentEl.scrollTop = 0;
  }
  function search(q) {
    q = q.trim().toLowerCase();
    if (!q) { showEntry(cGuide.items[0]); return; }
    var hits = [];
    Object.keys(entries).forEach(function (id) { if (entries[id].title.toLowerCase().indexOf(q) >= 0) hits.push(id); });
    var cards = hits.slice(0, 200).map(function (id) {
      return '<div class="card" data-go="' + esc(id) + '"><div class="n">' + esc(entries[id].title) + '</div><div class="d">' + esc(t(UI[entries[id].cat] || { en: entries[id].cat })) + '</div></div>';
    }).join('');
    contentEl.innerHTML = '<h1>' + t({ en: 'Search', de: 'Suche' }) + '</h1><div class="sub">' + hits.length + ' ' + t({ en: 'results', de: 'Treffer' }) + '</div>' +
      (hits.length ? '<div class="cards">' + cards + '</div>' : '<p class="empty">' + t({ en: 'No matches.', de: 'Keine Treffer.' }) + '</p>');
    contentEl.scrollTop = 0;
  }

  // Delegated clicks: cross-links (data-go) and category links (data-link="cat:xxx").
  document.addEventListener('click', function (e) {
    var go = e.target.closest('[data-go]');
    if (go) { showEntry(go.dataset.go); return; }
    var lk = e.target.closest('[data-link]');
    if (lk) { var v = lk.dataset.link; if (v.indexOf('cat:') === 0) { showCat(v.slice(4)); var w = navEl.querySelector('.cat[data-cat="' + v.slice(4) + '"]'); if (w) w.classList.add('open'); } else showEntry(v); }
  });
  var sTimer = null;
  searchEl.addEventListener('input', function () { clearTimeout(sTimer); sTimer = setTimeout(function () { search(searchEl.value); }, 120); });

  buildNav();
  // Open the guide by default.
  var guideWrap = navEl.querySelector('.cat[data-cat="guide"]'); if (guideWrap) guideWrap.classList.add('open');
  if (cGuide.items.length) showEntry(cGuide.items[0]); else showCat('tech');
})();
