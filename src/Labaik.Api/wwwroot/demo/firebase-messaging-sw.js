// Only public client settings are served by this route. This also initializes correctly after a worker restart.
importScripts('/demo/config.js');
importScripts('https://www.gstatic.com/firebasejs/13.0.0/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/13.0.0/firebase-messaging-compat.js');
firebase.initializeApp(globalThis.LABAIK_DEMO.firebase);
firebase.messaging();
// Background notification payloads are displayed by the SDK; no duplicate showNotification handler.
