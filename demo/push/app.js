/* This loopback receiver obtains an FCM token; it never receives backend/service-account credentials. */
const status = document.getElementById('status');
const enable = document.getElementById('enable');
const copy = document.getElementById('copy');
let listening = false;

function received(payload) {
  const notification = payload.notification || {};
  const row = document.createElement('li');
  row.textContent = `${new Date().toLocaleTimeString()} — ${notification.title || 'Message'}: ${notification.body || ''}`;
  document.getElementById('messages').prepend(row);
}

try {
  firebase.initializeApp(globalThis.LABAIK_PUSH.firebase);
  enable.disabled = false;
  status.textContent = 'Ready. Click Enable notifications and allow the browser permission.';
} catch {
  status.textContent = 'Firebase client could not initialize. Check the local configuration and CDN connection.';
}

enable.addEventListener('click', async () => {
  enable.disabled = true;
  try {
    if (await Notification.requestPermission() !== 'granted') {
      status.textContent = 'Notifications were denied. Allow them in this site’s browser settings, then retry.';
      return;
    }
    await navigator.serviceWorker.register('/firebase-messaging-sw.js');
    const registration = await navigator.serviceWorker.ready;
    const messaging = firebase.messaging();
    const token = await messaging.getToken({ vapidKey: globalThis.LABAIK_PUSH.vapidKey, serviceWorkerRegistration: registration });
    if (!token) throw new Error('NoRegistration');
    document.getElementById('token').value = token;
    copy.disabled = false;
    if (!listening) { messaging.onMessage(received); listening = true; }
    status.textContent = 'FCM token obtained. Copy it privately and register it with the API. Delivery is not verified yet.';
  } catch (error) {
    // Firebase's message may include configuration or request details; display only its fixed error category.
    status.textContent = `Registration failed (${error.code || 'browser/setup error'}). Check VAPID, project settings and browser support.`;
  } finally {
    enable.disabled = false;
  }
});

copy.addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText(document.getElementById('token').value);
    status.textContent = 'Token copied. Paste only into the private PowerShell registration prompt.';
  } catch {
    status.textContent = 'Clipboard access failed. Allow clipboard permission and retry.';
  }
});
