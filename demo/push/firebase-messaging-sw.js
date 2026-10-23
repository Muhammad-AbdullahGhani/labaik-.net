// Configuration must be available on worker restarts as well as initial registration.
// The local server serves only public web-client settings, never the Admin SDK JSON or API session.
importScripts('/demo-config.js');
importScripts('https://www.gstatic.com/firebasejs/13.0.0/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/13.0.0/firebase-messaging-compat.js');
firebase.initializeApp(globalThis.LABAIK_PUSH.firebase);
firebase.messaging();
// Firebase automatically displays background notification payloads. Do not show a second copy here.
