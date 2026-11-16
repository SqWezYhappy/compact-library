const { contextBridge, ipcRenderer: R } = require('electron');
contextBridge.exposeInMainWorld('api', {
  init: () => R.invoke('init'),
  scan: () => R.invoke('scan'),
  save: s => R.send('save', s),
  launch: a => R.send('launch', a),
  openUrl: u => R.send('openUrl', u),
  hide: () => R.send('hide'),
  power: a => R.send('power', a),
  autostart: v => R.send('autostart', v),
  hotkey: k => R.invoke('hotkey', k),
  applyIcons: () => R.invoke('icons', 'install'),
  restoreIcons: () => R.invoke('icons', 'restore'),
  openPacks: () => R.send('openPacks'),
  onShow: f => R.on('show', () => f()),
  onHide: f => R.on('hide', () => f()),
  onSettings: f => R.on('settings', () => f())
});
