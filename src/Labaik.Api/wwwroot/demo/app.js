'use strict';
const byId = id => document.getElementById(id);
const config = globalThis.LABAIK_DEMO || {};
let session = null;
let user = null;
let bookings = [];
let selected = null;
let rotation = null;
let refreshing = null;
let loading = false;
let pushRegistered = false;
let messaging = null;
let stopMessages = null;
const checkoutKeys = new Map();
const stages = ['Draft', 'PendingPayment', 'Paid', 'Offered', 'Accepted', 'InProgress', 'ProofSubmitted', 'Completed'];
const stageLabels = ['Draft', 'Checkout', 'Paid', 'Offered', 'Accepted', 'In progress', 'Proof', 'Completed'];

function node(tag, text, className) {
  const element = document.createElement(tag);
  if (text !== undefined) element.textContent = text;
  if (className) element.className = className;
  return element;
}
function notice(message, error = false) {
  const element = byId('notice'); element.textContent = message; element.classList.toggle('error', error); element.hidden = false;
}
function record(message) {
  const item = node('li'); const time = node('time', new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }));
  item.append(time, node('span', message)); byId('activity').prepend(item);
  while (byId('activity').children.length > 20) byId('activity').lastChild.remove();
}
function money(value, currency = 'SAR') {
  // DTOs carry minor amounts as strings, preserving long precision without a JavaScript floating-point price calculation.
  const amount = BigInt(value); return `${currency} ${(amount / 100n).toLocaleString('en')} .${(amount % 100n).toString().padStart(2, '0')}`.replace(' .', '.');
}
function badge(state) {
  return node('span', state.replace(/([a-z])([A-Z])/g, '$1 $2'), 'badge ' +
    (['Cancelled', 'Disputed', 'Failed'].includes(state) ? 'problem' : ['PendingPayment', 'Pending', 'Creating', 'Unknown'].includes(state) ? 'pending' : ''));
}
async function responseData(response) {
  const data = await response.json().catch(() => null);
  if (!response.ok) {
    if (response.status === 401) throw new Error('Session is not valid. Sign in again.');
    if (response.status === 403) throw new Error('This account does not have permission for that action.');
    if (response.status === 409) throw new Error(data?.reason || 'This action conflicts with the current booking state. Refresh and retry.');
    if (response.status === 429) throw new Error('Too many requests. Wait a minute before trying again.');
    if (response.status === 503) throw new Error('A dependency is unavailable. An uncertain payment must be recovered using the same checkout.');
    throw new Error(`Request failed (HTTP ${response.status}).`);
  }
  return data;
}
async function token() {
  if (!session) throw new Error('Sign in first.');
  if (Date.parse(session.accessExpiresAt) > Date.now() + 30000) return session.accessToken;
  if (!refreshing) refreshing = (async () => {
    // Preserve the same parent/key across a lost-response retry; a changed key would correctly revoke the family.
    rotation ||= { refreshToken: session.refreshToken, requestKey: crypto.randomUUID().replaceAll('-', '') };
    const response = await fetch('/auth/refresh', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(rotation) });
    if (response.status === 401) { session = null; showSession(); }
    session = await responseData(response); rotation = null;
    record('Local session renewed.');
  })().finally(() => { refreshing = null; });
  await refreshing;
  return session.accessToken;
}
async function api(path, method = 'GET', body) {
  const access = await token();
  const response = await fetch(path, { method, headers: { Authorization: `Bearer ${access}`, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  return responseData(response);
}
function showSession() {
  const authenticated = Boolean(session);
  byId('login-section').hidden = authenticated; byId('signed-in').hidden = !authenticated; byId('identity').hidden = !authenticated;
  byId('role-label').textContent = user?.role || '';
  const operator = authenticated && ['Admin', 'Accountant'].includes(user?.role);
  byId('operations-link').hidden = !operator; byId('operations-section').hidden = !operator;
}
async function action(button, run) {
  button.disabled = true;
  try { await run(); }
  catch (error) { notice(error.code ? `Firebase/browser request failed (${error.code}). Check configuration and permissions.` : error.message || 'The request could not complete. Check the service connection.', true); }
  finally { button.disabled = false; }
}

byId('login-form').addEventListener('submit', event => {
  event.preventDefault();
  action(byId('login-button'), async () => {
    try {
      const credential = await firebase.auth().signInWithEmailAndPassword(byId('email').value.trim(), byId('password').value);
      const idToken = await credential.user.getIdToken();
      session = await responseData(await fetch('/auth/firebase', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ idToken }) }));
      rotation = null; user = await api('/auth/me'); showSession();
      record('Firebase identity verified; local API session issued.');
      notice(`Signed in with ${user.role} access. ${config.vapidKey ? 'Enable browser push before paying if you want to demonstrate notifications.' : 'Push is not configured yet. You can continue with the booking and email demo.'}`);
      await refreshBookings();
    } catch (error) {
      // Provider messages can contain user input. Show a fixed Firebase code rather than the raw provider message.
      if (error.code) throw new Error(`Firebase sign-in failed (${error.code}). Check the demo account and project.`);
      throw error;
    } finally { byId('password').value = ''; }
  });
});

byId('logout').addEventListener('click', () => action(byId('logout'), async () => {
  if (pushRegistered && messaging) {
    try { await messaging.deleteToken(); pushRegistered = false; }
    catch { notice('Browser push could not be unregistered. Disable this site’s notification permission before sharing the device.', true); return; }
  }
  if (stopMessages) { stopMessages(); stopMessages = null; }
  await firebase.auth().signOut(); session = null; user = null; rotation = null; bookings = []; selected = null; checkoutKeys.clear();
  byId('activity').replaceChildren(); byId('push-messages').replaceChildren(); byId('push-status').textContent = 'Not registered in this session.';
  showSession(); notice('Signed out of this browser. Sign in again to continue.');
}));

function renderBookings() {
  const list = byId('bookings'); list.replaceChildren(); byId('booking-count').textContent = bookings.length;
  if (!bookings.length) list.append(node('p', 'No bookings yet. Create your first booking above.', 'empty'));
  for (const booking of bookings) {
    const button = node('button', undefined, 'booking-row' + (booking.id === selected ? ' selected' : ''));
    button.type = 'button'; button.setAttribute('aria-pressed', String(booking.id === selected));
    button.append(node('span', `Booking ${booking.id.slice(0, 8)}`, 'reference'), node('span', booking.serviceDate));
    const bottom = node('span', undefined, 'row-bottom'); bottom.append(badge(booking.state), node('span', money(booking.totalMinor, booking.currency)));
    button.append(bottom); button.addEventListener('click', () => { selected = booking.id; renderBookings(); renderDetail(); }); list.append(button);
  }
}
function addAction(container, label, secondary, run) {
  const button = node('button', label, secondary ? 'secondary' : ''); button.type = 'button';
  button.addEventListener('click', () => action(button, run)); container.append(button);
}
function renderDetail() {
  const booking = bookings.find(x => x.id === selected); if (!booking) return;
  const detail = byId('booking-detail'); detail.replaceChildren();
  const top = node('div', undefined, 'detail-top'); const identity = node('div'); identity.append(badge(booking.state), node('h3', `Booking ${booking.id.slice(0, 8)}`), node('span', booking.serviceDate, 'muted small'));
  const price = node('div', money(booking.totalMinor, booking.currency), 'price'); price.append(node('small', 'Server-calculated total')); top.append(identity, price); detail.append(top);
  const journey = node('ol', undefined, 'journey'); const current = stages.indexOf(booking.state);
  for (let i = 0; i < stages.length; i++) journey.append(node('li', stageLabels[i], i <= current ? 'done' : ''));
  detail.append(journey);
  const facts = node('dl', undefined, 'detail-facts');
  for (const [label, value] of [['Payment attempt', booking.paymentStatus || 'Not created'], ['Capture verified', booking.captured ? 'Yes' : 'No'], ['Refund', booking.refundStatus || 'Not requested'], ['Booking version', String(booking.version)]]) {
    const field = node('div'); field.append(node('dt', label), node('dd', value)); facts.append(field);
  }
  detail.append(facts);
  const actions = node('div', undefined, 'detail-actions');
  const canAct = booking.owned && ['Customer', 'Admin'].includes(user?.role);
  if (canAct && ['Draft', 'PendingPayment'].includes(booking.state)) addAction(actions, booking.attemptId ? 'Retry checkout' : 'Create checkout', false, async () => {
    if (!checkoutKeys.has(booking.id)) checkoutKeys.set(booking.id, crypto.randomUUID().replaceAll('-', ''));
    const attempt = await api(`/bookings/${booking.id}/checkout`, 'POST', { requestKey: checkoutKeys.get(booking.id) });
    record(`Checkout recovered or created for booking ${booking.id.slice(0, 8)}.`);
    notice(attempt.checkoutId ? 'Checkout is ready. Simulate payment next; a checkout alone is not proof of capture.' : 'The gateway outcome is unresolved. Retry the same checkout; do not create another booking to recover a charge.');
    await refreshBookings();
  });
  if (canAct && booking.checkoutId && !booking.captured) addAction(actions, booking.state === 'Cancelled' ? 'Simulate late payment' : 'Simulate successful payment', false, async () => {
    await api(`/demo/bookings/${booking.id}/pay`, 'POST', { outcome: 'Succeeded' });
    record('Fake gateway payment submitted. Capture verification runs in the worker.');
    notice('Simulation submitted. Wait for the verified booking status; no real money was charged.'); await refreshBookings();
  });
  if (canAct && ['Draft', 'PendingPayment', 'Paid', 'Offered'].includes(booking.state)) addAction(actions, 'Cancel booking', true, async () => {
    if (!confirm('Cancel this booking? A verified captured payment will require a full refund.')) return;
    await api(`/bookings/${booking.id}/cancel`, 'POST'); record('Cancellation saved. Captured money follows the durable refund workflow.'); await refreshBookings();
  });
  detail.append(actions);
  const explanation = node('div', undefined, 'note');
  explanation.append(node('strong', booking.captured ? 'Capture verified by the worker' : booking.checkoutId ? 'A checkout is not a capture' : 'Your quote is saved on the server'));
  explanation.append(node('p', booking.captured ? 'Capture, booking decisions and outgoing work were committed together. Notification delivery can finish later.' : booking.checkoutId ? 'The worker re-queries the gateway and validates the amount, currency and reference before applying the result.' : 'Create checkout once, then retry it safely if a response is lost. The gateway key stays on the server.'));
  detail.append(explanation);
}
async function refreshBookings() {
  if (!session || loading) return; loading = true;
  try {
    const previous = new Map(bookings.map(x => [x.id, x.state])); const next = await api('/demo/bookings');
    const changed = JSON.stringify(bookings) !== JSON.stringify(next); bookings = next;
    if (!selected || !bookings.some(x => x.id === selected)) selected = bookings[0]?.id || null;
    for (const booking of bookings) if (previous.has(booking.id) && previous.get(booking.id) !== booking.state) record(`Booking ${booking.id.slice(0, 8)} changed to ${booking.state}.`);
    if (changed) { renderBookings(); renderDetail(); }
  } finally { loading = false; }
}
byId('refresh').addEventListener('click', () => action(byId('refresh'), refreshBookings));
byId('booking-form').addEventListener('submit', event => {
  event.preventDefault(); action(event.submitter, async () => {
    const booking = await api('/bookings', 'POST', { serviceDate: byId('service-date').value, voucherCode: byId('voucher').value.trim() || null });
    selected = booking.id; record('Draft booking created with a server-calculated quote.'); notice('Booking created. Continue with checkout.'); await refreshBookings();
  });
});

byId('enable-push').addEventListener('click', () => action(byId('enable-push'), async () => {
  if (!config.vapidKey) throw new Error('Add the public Web Push key with start-demo-ui.ps1, then reload and sign in.');
  if (!('Notification' in window) || !('serviceWorker' in navigator)) throw new Error('This browser does not support the push demo. Use desktop Chrome or Edge on localhost.');
  if (await Notification.requestPermission() !== 'granted') throw new Error('Allow notifications for this site in the browser settings, then retry.');
  await navigator.serviceWorker.register('/demo/firebase-messaging-sw.js');
  const registration = await navigator.serviceWorker.ready;
  if (!registration) throw new Error('The notification worker is not registered yet. Retry once.');
  messaging ||= firebase.messaging();
  const deviceToken = await messaging.getToken({ vapidKey: config.vapidKey, serviceWorkerRegistration: registration });
  if (!deviceToken) throw new Error('No device token was issued. Check the Firebase Web Push configuration.');
  await api('/notifications/device', 'POST', { token: deviceToken, locale: byId('locale').value });
  if (!pushRegistered) stopMessages = messaging.onMessage(payload => {
    if (!session) return;
    const message = payload.notification || {}; byId('push-messages').prepend(node('li', `${message.title || 'Message'} — ${message.body || ''}`));
    byId('push-status').textContent = 'Push received in this browser.'; record('A real FCM message reached the browser.');
  });
  pushRegistered = true; byId('push-status').textContent = 'Registered. Receipt is confirmed only when a message appears below.';
  record('Browser recipient registered for outbox delivery.'); notice('Push is enabled. Use a fresh payment event to test both email and push.');
}));

byId('load-operations').addEventListener('click', () => action(byId('load-operations'), async () => {
  const [operations, payments, jobs] = await Promise.all([api('/operations'), api('/demo/payments'), api('/demo/jobs')]);
  const table = node('table'); const head = node('tr'); for (const label of ['Booking', 'Captured', 'Amount', 'Refund']) head.append(node('th', label));
  const thead = node('thead'); thead.append(head); table.append(thead); const tbody = node('tbody');
  for (const payment of payments) { const row = node('tr'); for (const value of [payment.bookingId.slice(0, 8), new Date(payment.capturedAt).toLocaleString(), money(payment.amountMinor, payment.currency), payment.refundStatus || 'Not requested']) row.append(node('td', value)); tbody.append(row); }
  table.append(tbody); byId('payments-table').replaceChildren(payments.length ? table : node('p', 'No captures recorded.', 'muted'));
  byId('reports').replaceChildren(); for (const report of operations.reports) {
    const row = node('div', undefined, 'report-row'); let result = null; try { result = JSON.parse(report.resultJson); } catch { /* Pending jobs have no result. */ }
    row.append(node('strong', new Date(report.scheduledFor).toLocaleDateString()), node('p', result ? `${result.MismatchCount} mismatches · ${result.GatewayCount} gateway captures checked` : 'No completed comparison result.'));
    for (const mismatch of result?.Mismatches || []) row.append(node('p', `${mismatch.Kind} · booking ${mismatch.BookingId.slice(0, 8)}`)); byId('reports').append(row);
  }
  if (!operations.reports.length) byId('reports').append(node('p', 'No reconciliation run recorded.', 'muted'));
  byId('dead-letters').replaceChildren();
  const health = node('div', undefined, 'report-row'); health.append(node('strong', jobs.counts.map(x => `${x.status}: ${x.count}`).join(' · ')), node('p', jobs.latestHeartbeatAt ? `Latest worker heartbeat: ${new Date(jobs.latestHeartbeatAt).toLocaleString()}` : 'No worker heartbeat recorded.'));
  byId('dead-letters').append(health);
  for (const job of operations.deadLetters) {
    const row = node('div', undefined, 'report-row'); row.append(node('strong', `Job ${job.id.slice(0, 8)}`), node('p', `${job.attempts} attempts · ${job.lastError || 'Needs investigation'}`)); byId('dead-letters').append(row);
  }
  if (!operations.deadLetters.length) byId('dead-letters').append(node('p', 'No dead-letter jobs in the current result.', 'muted'));
  record('Privileged payment ledger and operations report loaded.');
}));

const today = new Date(); today.setDate(today.getDate() + 1);
const nextDate = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
byId('service-date').value = nextDate;
try {
  if (!config.firebase?.apiKey || !config.firebase?.appId) throw new Error('MissingPublicConfig');
  firebase.initializeApp(config.firebase);
  firebase.auth().setPersistence(firebase.auth.Auth.Persistence.NONE).then(() => {
    byId('login-button').disabled = false; byId('setup-message').textContent = 'Session credentials stay in memory. Reloading requires sign-in.';
  }).catch(() => { byId('setup-message').textContent = 'Could not initialize in-memory sign-in. Check the browser settings.'; });
} catch { byId('setup-message').textContent = 'Run scripts/start-demo-ui.ps1 to configure the public Firebase Web app settings.'; }
setInterval(() => {
  if (session && !document.hidden) refreshBookings().catch(error => notice(error.message || 'Status refresh failed.', true));
}, 5000);
