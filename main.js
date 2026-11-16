const { app, BrowserWindow, Tray, Menu, globalShortcut, ipcMain, screen, shell, nativeImage } = require('electron');
const path = require('path'), fs = require('fs'), { execFile, spawn } = require('child_process'), { pathToFileURL } = require('url');
if (!app.requestSingleInstanceLock()) app.quit();
app.setAppUserModelId('local.purple.library');
const DATA = path.join(app.getPath('userData'), 'purple-library.json');
const PACKS = path.join(app.getPath('appData'), 'PurpleLibrary', 'IconPacks');
const PACK_SRC = app.isPackaged ? path.join(process.resourcesPath, 'GlassPhone') : path.join(__dirname, 'GlassPhone');
let win, tray, state = { cfg: {}, use: {}, p: [], hid: [], off: [] };
try { state = Object.assign(state, JSON.parse(fs.readFileSync(DATA, 'utf8'))); } catch {}
const save = () => { try { fs.mkdirSync(path.dirname(DATA), { recursive: true }); fs.writeFileSync(DATA, JSON.stringify(state)); } catch {} };

const walk = (d, out = []) => { try { for (const e of fs.readdirSync(d, { withFileTypes: true })) { const p = path.join(d, e.name); e.isDirectory() ? walk(p, out) : /\.lnk$/i.test(e.name) && out.push(p); } } catch {} return out; };
const ps = cmd => new Promise(r => execFile('powershell.exe', ['-NoProfile', '-Command', '[Console]::OutputEncoding=[Text.Encoding]::UTF8;' + cmd], { maxBuffer: 1 << 25, windowsHide: true }, (e, o) => r(e ? '' : o)));
async function scan() {
  const seen = new Set(), apps = [];
  const dirs = [path.join(process.env.ProgramData || '', 'Microsoft/Windows/Start Menu/Programs'), path.join(app.getPath('appData'), 'Microsoft/Windows/Start Menu/Programs')];
  for (const d of dirs) for (const p of walk(d)) { const n = path.basename(p, '.lnk'), k = n.toLowerCase(); if (!seen.has(k)) { seen.add(k); apps.push({ id: p.toLowerCase(), n, kind: 'lnk', path: p }); } }
  try { let j = JSON.parse((await ps('Get-StartApps | ConvertTo-Json -Compress')) || '[]'); if (!Array.isArray(j)) j = [j];
    for (const a of j) if (a.AppID && a.AppID.includes('!') && !seen.has(a.Name.toLowerCase())) { seen.add(a.Name.toLowerCase()); apps.push({ id: a.AppID, n: a.Name, kind: 'uwp', path: a.AppID }); } } catch {}
  return { apps, games: await steamGames() };
}
async function steamGames() {
  let root = ''; const o = await new Promise(r => execFile('reg', ['query', 'HKCU\\Software\\Valve\\Steam', '/v', 'SteamPath'], { windowsHide: true }, (e, s) => r(s || '')));
  const m = o.match(/SteamPath\s+REG_SZ\s+(.+)/); root = m ? m[1].trim() : 'C:/Program Files (x86)/Steam';
  const libs = new Set([root]); try { for (const x of fs.readFileSync(path.join(root, 'steamapps/libraryfolders.vdf'), 'utf8').matchAll(/"path"\s+"([^"]+)"/g)) libs.add(x[1].replace(/\\\\/g, '\\')); } catch {}
  const g = [];
  for (const l of libs) { const d = path.join(l, 'steamapps'); try { for (const f of fs.readdirSync(d)) if (/^appmanifest_\d+\.acf$/.test(f)) { const t = fs.readFileSync(path.join(d, f), 'utf8'), id = (t.match(/"appid"\s+"(\d+)"/) || [])[1], n = (t.match(/"name"\s+"([^"]+)"/) || [])[1];
    if (id && n && !/redistributable|steamworks|proton|runtime|linux/i.test(n)) g.push({ id: 'steam:' + id, n, kind: 'steam', path: id }); } } catch {} }
  return g;
}
function packs() {
  const r = {}; try { for (const d of fs.readdirSync(PACKS, { withFileTypes: true })) if (d.isDirectory()) { const m = {}; for (const f of fs.readdirSync(path.join(PACKS, d.name))) if (/\.png$/i.test(f)) m[f.slice(0, -4).toLowerCase()] = pathToFileURL(path.join(PACKS, d.name, f)).href; r[d.name] = m; } } catch {}
  return r;
}
function place() {
  const d = screen.getDisplayNearestPoint(screen.getCursorScreenPoint());
  win.setBounds(d.workArea);
}
function openLib() { place(); win.show(); win.focus(); win.webContents.send('show'); }
function toggleLib() { win.isVisible() ? win.webContents.send('hide') : openLib(); }
function regHotkey(k) { globalShortcut.unregisterAll(); try { return globalShortcut.register(k, toggleLib); } catch { return false; } }
app.whenReady().then(() => {
  win = new BrowserWindow({ show: false, frame: false, transparent: true, backgroundColor: '#00000000', skipTaskbar: true, alwaysOnTop: true, resizable: false, hasShadow: false, focusable: true,
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true, nodeIntegration: false } });
  win.setAlwaysOnTop(true, 'pop-up-menu');
  win.loadFile(path.join(__dirname, 'renderer/index.html'));
  win.on('blur', () => { if (win.isVisible() && state.cfg.autoHide !== false && !win.webContents.isDevToolsOpened()) win.webContents.send('hide'); });
  tray = new Tray(nativeImage.createFromPath(path.join(__dirname, 'assets/icon.png')).resize({ width: 20, height: 20 }));
  tray.setToolTip('Purple Library');
  tray.setContextMenu(Menu.buildFromTemplate([{ label: 'Открыть библиотеку', click: openLib }, { label: 'Настройки', click: () => { openLib(); setTimeout(() => win.webContents.send('settings'), 300); } }, { type: 'separator' }, { label: 'Выход', click: () => app.quit() }]));
  tray.on('click', toggleLib);
  regHotkey(state.cfg.hotkey || 'Ctrl+Alt+Space');
  if (process.argv.includes('--show')) win.webContents.once('did-finish-load', () => setTimeout(openLib, 300));
});
app.on('second-instance', () => win && openLib());
app.on('will-quit', () => globalShortcut.unregisterAll());
app.on('window-all-closed', e => e.preventDefault());

ipcMain.handle('init', async () => ({ ...(await scan()), state, packs: packs(), cfg: state.cfg, auto: app.getLoginItemSettings().openAtLogin }));
ipcMain.handle('scan', async () => ({ ...(await scan()), state, packs: packs() }));
ipcMain.on('save', (_, s) => { state = { ...state, ...s }; save(); });
ipcMain.on('openUrl', (_, u) => { if (/^https?:\/\//.test(u)) shell.openExternal(u); });
ipcMain.on('hide', () => win.hide());
ipcMain.on('launch', (_, a) => {
  if (a.kind === 'steam') shell.openExternal('steam://rungameid/' + a.path);
  else if (a.kind === 'uwp') spawn('explorer.exe', ['shell:AppsFolder\\' + a.path], { detached: true, stdio: 'ignore' }).unref();
  else shell.openPath(a.path);
});
ipcMain.on('power', (_, a) => {
  if (a === 'sleep') spawn('rundll32.exe', ['powrprof.dll,SetSuspendState', '0,1,0'], { detached: true });
  else spawn('shutdown', a === 'restart' ? ['/r', '/t', '0'] : ['/s', '/t', '0'], { detached: true });
});
ipcMain.on('autostart', (_, v) => app.setLoginItemSettings({ openAtLogin: !!v }));
ipcMain.handle('hotkey', (_, k) => { const ok = regHotkey(k); if (!ok) regHotkey(state.cfg.hotkey || 'Ctrl+Alt+Space'); else { state.cfg.hotkey = k; save(); } return ok; });
ipcMain.on('openPacks', () => { fs.mkdirSync(PACKS, { recursive: true }); shell.openPath(PACKS); });
ipcMain.handle('icons', (_, what) => new Promise(r => {
  const f = path.join(PACK_SRC, what === 'install' ? 'install.ps1' : 'restore.ps1');
  execFile('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', f], { windowsHide: true }, e => r(e ? 'Ошибка: ' + e.message : what === 'install' ? 'Иконки применены' : 'Иконки возвращены'));
}));
