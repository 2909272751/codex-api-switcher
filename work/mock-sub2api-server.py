import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


EXPECTED_AUTH = "Bearer sub2-test-token-not-a-real-key"


class Handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def send_json(self, status, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def authorized(self):
        if self.headers.get("Authorization") == EXPECTED_AUTH:
            return True
        self.send_json(401, {"error": {"message": "wrong test bearer token"}})
        return False

    def do_GET(self):
        if not self.authorized():
            return
        if self.path == "/v1/models":
            self.send_json(200, {"data": [{"id": "sub2-test-model"}]})
            return
        self.send_json(404, {"error": {"message": "not found"}})

    def do_POST(self):
        if not self.authorized():
            return
        length = int(self.headers.get("Content-Length", "0"))
        payload = json.loads(self.rfile.read(length) or b"{}")
        if payload.get("model") != "sub2-test-model":
            self.send_json(400, {"error": {"message": "wrong test model"}})
            return
        if self.path == "/v1/responses":
            body = b'data: {"type":"response.completed"}\n\n'
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if self.path == "/v1/responses/compact":
            self.send_json(200, {"status": "completed"})
            return
        self.send_json(404, {"error": {"message": "not found"}})


def main():
    port_file = sys.argv[1]
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    with open(port_file, "w", encoding="ascii") as handle:
        handle.write(str(server.server_port))
    server.serve_forever()


if __name__ == "__main__":
    main()
