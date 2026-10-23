"""Serve a minimal FCM recipient on loopback, with no repository browsing or access logs."""
from functools import partial
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import argparse
import getpass
import json

ROOT = Path(__file__).resolve().parents[1]
CONFIG = ROOT / '.env.push-demo.json'
ASSETS = ROOT / 'demo' / 'push'


def validate(settings):
    # Only allow explicit web-client fields. Accidentally pasting Admin credentials must never serve them.
    if set(settings) != {'firebase', 'vapidKey'} or not isinstance(settings['firebase'], dict):
        raise ValueError('Expected web-client configuration only.')
    if set(settings['firebase']) != {'apiKey', 'projectId', 'messagingSenderId', 'appId'}:
        raise ValueError('Use only the four requested Firebase web-client fields, not service-account JSON.')
    values = [*settings['firebase'].values(), settings['vapidKey']]
    if any(not isinstance(value, str) or not value.strip() or '\n' in value or '\r' in value for value in values):
        raise ValueError('All web-client fields and the public VAPID key are required.')
    if any('PRIVATE KEY' in value for value in values):
        raise ValueError('Private credentials cannot be used by this browser receiver.')
    return settings


class Handler(BaseHTTPRequestHandler):
    def __init__(self, *args, settings, **kwargs):
        self.settings = settings
        super().__init__(*args, **kwargs)

    def do_GET(self):
        allowed_hosts = {f'localhost:{self.server.server_port}', f'127.0.0.1:{self.server.server_port}'}
        if self.headers.get('Host') not in allowed_hosts:
            self.send_error(403)
            return
        if self.path == '/demo-config.js':
            body = ('globalThis.LABAIK_PUSH = ' + json.dumps(self.settings) + ';').encode()
            content_type = 'application/javascript'
        else:
            files = {'/': ('index.html', 'text/html'), '/app.js': ('app.js', 'application/javascript'),
                     '/style.css': ('style.css', 'text/css'),
                     '/firebase-messaging-sw.js': ('firebase-messaging-sw.js', 'application/javascript')}
            if self.path not in files:
                self.send_error(404)
                return
            name, content_type = files[self.path]
            body = (ASSETS / name).read_bytes()
        self.send_response(200)
        self.send_header('Content-Type', content_type + '; charset=utf-8')
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Referrer-Policy', 'no-referrer')
        self.send_header('Content-Security-Policy', "default-src 'self'; script-src 'self' https://www.gstatic.com; connect-src 'self' https://*.googleapis.com; worker-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass  # Request paths and query strings must not become accidental credential logs.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--configure', action='store_true', help='Prompt for public web-client settings and save an ignored local file.')
    parser.add_argument('--port', type=int, default=8090)
    args = parser.parse_args()
    if args.configure:
        settings = validate({'firebase': {
            'apiKey': getpass.getpass('Firebase Web API key (hidden): ').strip(),
            'projectId': input('Firebase project ID: ').strip(),
            'messagingSenderId': input('Web config messagingSenderId: ').strip(),
            'appId': input('Web config appId: ').strip()
        }, 'vapidKey': input('Web Push certificates PUBLIC key: ').strip()})
        CONFIG.write_text(json.dumps(settings), encoding='utf-8')
    else:
        if not CONFIG.exists():
            raise SystemExit('Run once with --configure. Never supply service-account JSON.')
        settings = validate(json.loads(CONFIG.read_text(encoding='utf-8')))
    server = ThreadingHTTPServer(('127.0.0.1', args.port), partial(Handler, settings=settings))
    print(f'Push receiver: http://localhost:{args.port} (keep this terminal running; Ctrl+C stops it).')
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == '__main__':
    main()
