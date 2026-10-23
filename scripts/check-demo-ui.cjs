/* Local browser interaction checks with synthetic API/Firebase responses. Not a live-provider test.
   Setup: npm install --prefix artifacts/ui-check playwright; then node scripts/check-demo-ui.cjs.
   Uses installed Edge, or set PLAYWRIGHT_BROWSER_CHANNEL to a Playwright-supported browser channel. */
const { chromium } = require('../artifacts/ui-check/node_modules/playwright');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.join(__dirname, '../src/Labaik.Api/wwwroot/demo');
const bookingId = '00000000-0000-0000-0000-000000000011';
let booking = null;
const checkoutKeys = [];
const browserErrors = [];
function json(response, value, status = 200) { response.writeHead(status, { 'Content-Type': 'application/json' }); response.end(JSON.stringify(value)); }
const server = http.createServer(async (request, response) => {
  const chunks = []; for await (const chunk of request) chunks.push(chunk);
  const raw = Buffer.concat(chunks).toString(); const body = raw ? JSON.parse(raw) : null;
  assert.equal(request.headers['x-gateway-key'], undefined, 'The browser must never carry the private gateway key.');
  if (request.url === '/demo/config.js') { response.setHeader('Content-Type', 'application/javascript'); return response.end('globalThis.LABAIK_DEMO={firebase:{apiKey:"synthetic-public-key",appId:"synthetic-app",projectId:"synthetic-project"},vapidKey:""};'); }
  if (request.url === '/auth/firebase') { assert.equal(body.idToken, 'synthetic-firebase-token'); return json(response, { accessToken: 'synthetic-api-access', refreshToken: 'synthetic-refresh', accessExpiresAt: new Date(Date.now() + 600000).toISOString() }); }
  if (request.url.startsWith('/auth/') || request.url.startsWith('/bookings') || request.url.startsWith('/demo/bookings')) {
    if (request.headers.authorization !== 'Bearer synthetic-api-access') return json(response, {}, 401);
    if (request.url === '/auth/me') return json(response, { userId: 'synthetic-user', role: 'Customer' });
    if (request.url === '/demo/bookings') return json(response, booking ? [booking] : []);
    if (request.url === '/bookings') {
      assert.deepEqual(Object.keys(body).sort(), ['serviceDate', 'voucherCode']);
      booking = { id: bookingId, serviceDate: body.serviceDate, totalMinor: '10000', currency: 'SAR', state: 'Draft', owned: true, version: 1, captured: false };
      return json(response, { id: bookingId });
    }
    if (request.url === `/bookings/${bookingId}/checkout`) {
      checkoutKeys.push(body.requestKey); booking.state = 'PendingPayment'; booking.attemptId = 'synthetic-attempt'; booking.checkoutId = 'synthetic-checkout'; booking.paymentStatus = 'Pending'; booking.version++;
      return json(response, { id: booking.attemptId, checkoutId: booking.checkoutId });
    }
    if (request.url === `/demo/bookings/${bookingId}/pay`) {
      assert.equal(body.outcome, 'Succeeded'); booking.state = 'Paid'; booking.captured = true; booking.paymentStatus = 'Succeeded'; booking.version++;
      return json(response, { status: 'Submitted' }, 202);
    }
  }
  const assets = { '/demo/': 'index.html', '/demo/style.css': 'style.css', '/demo/app.js': 'app.js' };
  if (!assets[request.url]) { response.writeHead(404); return response.end(); }
  const extension = path.extname(assets[request.url]); response.setHeader('Content-Type', extension === '.html' ? 'text/html' : extension === '.css' ? 'text/css' : 'application/javascript');
  response.end(fs.readFileSync(path.join(root, assets[request.url])));
});

(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  let browser;
  try {
    browser = await chromium.launch({ channel: process.env.PLAYWRIGHT_BROWSER_CHANNEL || 'msedge', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
    await context.route('https://www.gstatic.com/**', route => route.fulfill({ contentType: 'application/javascript', body: '' }));
    await context.addInitScript(() => {
      const auth = { setPersistence: async () => {}, signInWithEmailAndPassword: async () => ({ user: { getIdToken: async () => 'synthetic-firebase-token' } }), signOut: async () => {} };
      globalThis.firebase = { initializeApp() {}, auth: Object.assign(() => auth, { Auth: { Persistence: { NONE: 'none' } } }) };
    });
    const page = await context.newPage(); page.on('pageerror', error => browserErrors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/demo/`);
    await page.locator('#email').fill('demo-user@example.invalid'); await page.locator('#password').fill('synthetic-password');
    await page.locator('#login-button').click(); await page.locator('#signed-in').waitFor({ state: 'visible' });
    await page.locator('#booking-form button').click(); await page.getByRole('button', { name: 'Create checkout', exact: true }).waitFor();
    await page.getByRole('button', { name: 'Create checkout', exact: true }).click(); await page.getByRole('button', { name: 'Retry checkout', exact: true }).waitFor();
    await page.getByRole('button', { name: 'Retry checkout', exact: true }).click();
    await page.waitForFunction(() => document.getElementById('activity').children.length >= 4);
    assert.equal(checkoutKeys.length, 2); assert.equal(checkoutKeys[0], checkoutKeys[1], 'Checkout retry must preserve its key.');
    await page.getByRole('button', { name: 'Simulate successful payment', exact: true }).click();
    await page.waitForFunction(() => document.getElementById('booking-detail').textContent.includes('Capture verified by the worker'));
    assert.ok((await page.locator('#booking-detail').innerText()).includes('SAR 100.00'));
    assert.equal(await page.locator('#operations-section').isVisible(), false);
    assert.equal(await page.evaluate(() => Object.keys(localStorage).length), 0);
    assert.equal((await page.locator('body').innerText()).includes('synthetic-api-access'), false);
    const folder = path.join(__dirname, '../artifacts/ui-check'); fs.mkdirSync(folder, { recursive: true });
    await page.screenshot({ path: path.join(folder, 'desktop.png'), fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'Mobile layout must not overflow horizontally.');
    await page.screenshot({ path: path.join(folder, 'mobile.png'), fullPage: true });
    await page.locator('#logout').click(); await page.locator('#login-section').waitFor({ state: 'visible' });
    assert.deepEqual(browserErrors, []);
    console.log('Browser checks passed: login, server-price request, stable checkout retry, paid status, role visibility, memory-only session and mobile layout. Synthetic provider/API responses only.');
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
