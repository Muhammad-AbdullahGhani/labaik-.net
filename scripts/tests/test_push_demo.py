"""Offline safety/smoke checks for the loopback-only push receiver; these do not claim live FCM delivery."""
import importlib.util
import json
from pathlib import Path
from functools import partial
from http.server import ThreadingHTTPServer
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

spec = importlib.util.spec_from_file_location('push_demo', Path(__file__).resolve().parents[1] / 'serve-push-demo.py')
demo = importlib.util.module_from_spec(spec)
spec.loader.exec_module(demo)


class PushDemoTests(unittest.TestCase):
    def settings(self):
        return {'firebase': {'apiKey': 'synthetic-public-key', 'projectId': 'demo-project',
                             'messagingSenderId': '1234', 'appId': 'synthetic-app'}, 'vapidKey': 'synthetic-public-vapid'}

    def test_admin_credentials_are_rejected_instead_of_served(self):
        settings = self.settings()
        settings['firebase']['private_key'] = 'synthetic-private-material'
        with self.assertRaises(ValueError):
            demo.validate(settings)
        settings = self.settings()
        settings['firebase']['apiKey'] = '-----BEGIN PRIVATE KEY-----'
        with self.assertRaises(ValueError):
            demo.validate(settings)

    def test_receiver_serves_only_its_assets_and_public_config_without_logging(self):
        server = ThreadingHTTPServer(('127.0.0.1', 0), partial(demo.Handler, settings=demo.validate(self.settings())))
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        root = f'http://127.0.0.1:{server.server_port}'
        try:
            for path in ['/', '/app.js', '/style.css', '/firebase-messaging-sw.js', '/demo-config.js']:
                with urlopen(root + path, timeout=3) as response:
                    self.assertEqual(200, response.status)
                    self.assertEqual('no-store', response.headers['Cache-Control'])
                    self.assertIn("frame-ancestors 'none'", response.headers['Content-Security-Policy'])
            for path in ['/.env', '/../.env', '/config?token=synthetic']:
                with self.assertRaises(HTTPError) as result:
                    urlopen(root + path, timeout=3)
                self.assertEqual(404, result.exception.code)
            with self.assertRaises(HTTPError) as result:
                urlopen(Request(root, headers={'Host': 'untrusted.invalid'}), timeout=3)
            self.assertEqual(403, result.exception.code)
        finally:
            server.shutdown()
            server.server_close()
            worker.join(timeout=3)


if __name__ == '__main__':
    unittest.main()
