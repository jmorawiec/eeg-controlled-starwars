import socket

HOST = "127.0.0.1"
PORT = 5555

with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((HOST, PORT))
    server.listen(1)

    print(f"Nasłuchiwanie na {HOST}:{PORT}")

    while True:
        conn, addr = server.accept()
        print("Połączono:", addr)

        with conn:
            while True:
                data = conn.recv(4096)
                if not data:
                    break
                print("Odebrano:", data.decode(errors="replace"))