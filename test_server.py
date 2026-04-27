import socket
import struct
import sys
import ssl
import os
import subprocess
import tempfile
from datetime import datetime, timedelta
from cryptography import x509
from cryptography.x509.oid import NameOID
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa

PROXY_PROTO_V2_SIG = b'\x0D\x0A\x0D\x0A\x00\x0D\x0A\x51\x55\x49\x54\x0A'

def generate_self_signed_cert():
    from cryptography import x509
    from cryptography.x509.oid import NameOID
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    import datetime
    
    key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
    )
    
    subject = issuer = x509.Name([
        x509.NameAttribute(NameOID.COUNTRY_NAME, "CN"),
        x509.NameAttribute(NameOID.STATE_OR_PROVINCE_NAME, "Test"),
        x509.NameAttribute(NameOID.LOCALITY_NAME, "Test"),
        x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Test"),
        x509.NameAttribute(NameOID.COMMON_NAME, "localhost"),
    ])
    
    cert = x509.CertificateBuilder().subject_name(
        subject
    ).issuer_name(
        issuer
    ).public_key(
        key.public_key()
    ).serial_number(
        x509.random_serial_number()
    ).not_valid_before(
        datetime.datetime.now(datetime.timezone.utc)
    ).not_valid_after(
        datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=365)
    ).sign(key, hashes.SHA256())
    
    return key, cert

def parse_proxy_protocol_v1(data):
    end = data.find(b'\r\n')
    if end == -1:
        return None, data
    header = data[:end].decode('ascii')
    parts = header.split(' ')
    if len(parts) < 6 or parts[0] != 'PROXY':
        return None, data
    return {
        'version': 'v1',
        'protocol': parts[1],
        'src_ip': parts[2],
        'dst_ip': parts[3],
        'src_port': parts[4],
        'dst_port': parts[5]
    }, data[end+2:]

def parse_proxy_protocol_v2(data):
    if len(data) < 16:
        return None, data
    ver_cmd = data[12]
    fam = data[13]
    addr_len = struct.unpack('!H', data[14:16])[0]
    
    if len(data) < 16 + addr_len:
        return None, data
    
    version = (ver_cmd >> 4) & 0x0F
    cmd = ver_cmd & 0x0F
    family = (fam >> 4) & 0x0F
    
    addr_data = data[16:16+addr_len]
    
    if family == 1:
        src_ip = '.'.join(str(b) for b in addr_data[0:4])
        dst_ip = '.'.join(str(b) for b in addr_data[4:8])
        src_port = struct.unpack('!H', addr_data[8:10])[0]
        dst_port = struct.unpack('!H', addr_data[10:12])[0]
    elif family == 2:
        src_ip = ':'.join(f'{addr_data[i]*256+addr_data[i+1]:02x}' for i in range(0, 8, 2))
        dst_ip = ':'.join(f'{addr_data[i]*256+addr_data[i+1]:02x}' for i in range(8, 16, 2))
        src_port = struct.unpack('!H', addr_data[16:18])[0]
        dst_port = struct.unpack('!H', addr_data[18:20])[0]
    else:
        return {'version': 'v2', 'raw': data[:16+addr_len].hex()}, data[16+addr_len:]
    
    return {
        'version': 'v2',
        'src_ip': src_ip,
        'dst_ip': dst_ip,
        'src_port': src_port,
        'dst_port': dst_port
    }, data[16+addr_len:]

def parse_proxy_protocol(data):
    if data.startswith(b'PROXY '):
        return parse_proxy_protocol_v1(data)
    elif data.startswith(PROXY_PROTO_V2_SIG):
        return parse_proxy_protocol_v2(data)
    return None, data

def run_server(port=7813):
    server = socket.socket(socket.AF_INET6, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_V6ONLY, 0)
    server.bind(('::', port))
    server.listen(5)
    
    key, cert = generate_self_signed_cert()
    
    key_pem = key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.TraditionalOpenSSL,
        encryption_algorithm=serialization.NoEncryption()
    )
    cert_pem = cert.public_bytes(serialization.Encoding.PEM)
    
    import tempfile
    with tempfile.NamedTemporaryFile(mode='wb', suffix='.pem', delete=False) as key_file:
        key_file.write(key_pem)
        key_path = key_file.name
    
    with tempfile.NamedTemporaryFile(mode='wb', suffix='.pem', delete=False) as cert_file:
        cert_file.write(cert_pem)
        cert_path = cert_file.name
    
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.load_cert_chain(certfile=cert_path, keyfile=key_path)
    
    print(f"Listening on [::]:{port} (IPv4 + IPv6) with HTTPS")
    print("Press Ctrl+C to stop")
    print()
    
    try:
        while True:
            client, addr = server.accept()
            print("=" * 60)
            print(f"Connection from [{addr[0]}]:{addr[1]}")
            print("-" * 60)
            try:
                ssl_client = ctx.wrap_socket(client, server_side=True)
                data = ssl_client.recv(65535)
                if data:
                    proxy_info, remaining = parse_proxy_protocol(data)
                    if proxy_info:
                        print(f"[PROXY PROTOCOL {proxy_info.get('version', 'unknown')}]")
                        for k, v in proxy_info.items():
                            if k != 'version':
                                print(f"  {k}: {v}")
                        print("-" * 60)
                        if remaining:
                            print(remaining.decode('utf-8', errors='replace'))
                    else:
                        print(data.decode('utf-8', errors='replace'))
                    ssl_client.send(b'HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK')
                ssl_client.close()
            except Exception as e:
                print(f"Error: {e}")
            finally:
                client.close()
            print("=" * 60)
            print()
    except KeyboardInterrupt:
        print("\nServer stopped")
    finally:
        server.close()

if __name__ == '__main__':
    run_server(7813)
