#!/usr/bin/env python3
"""
Repository Secure Client
用于访问使用Secure模式保护的Repository目录
"""

import requests
import json
import time
import os
import base64
import struct
import argparse
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.backends import default_backend
from cryptography.hazmat.primitives import hmac


class SecureClient:
    def __init__(self, server_url: str, client_id: str, shared_token: str, verify_ssl: bool = False):
        self.server_url = server_url.rstrip('/')
        self.client_id = client_id
        self.shared_token = shared_token.encode('utf-8')
        self.session = requests.Session()
        self.session.verify = verify_ssl
        self.session_id = None
        
        self.private_key = None
        self.public_key = None
        self.server_public_key = None
        
        self._generate_key_pair()
    
    def _generate_key_pair(self):
        """生成RSA密钥对"""
        self.private_key = rsa.generate_private_key(
            public_exponent=65537,
            key_size=2048,
            backend=default_backend()
        )
        self.public_key = self.private_key.public_key()
        print(f"[+] 已生成RSA密钥对")
    
    def _get_public_key_pem(self) -> str:
        """获取PEM格式的公钥"""
        pem = self.public_key.public_bytes(
            encoding=serialization.Encoding.PEM,
            format=serialization.PublicFormat.SubjectPublicKeyInfo
        )
        return pem.decode('utf-8')
    
    def _load_server_public_key(self, pem_str: str):
        """加载服务器公钥"""
        self.server_public_key = serialization.load_pem_public_key(
            pem_str.encode('utf-8'),
            backend=default_backend()
        )
    
    def register(self) -> bool:
        """注册客户端到服务器"""
        print(f"[*] 正在注册客户端: {self.client_id}")
        
        resp = self.session.get(f"{self.server_url}/api/keys/server")
        if resp.status_code != 200:
            print(f"[-] 获取服务器公钥失败: {resp.status_code}")
            return False
        
        data = resp.json()
        if not data.get('success'):
            print(f"[-] 获取服务器公钥失败")
            return False
        
        self._load_server_public_key(data['publicKey'])
        print(f"[+] 已获取服务器公钥")
        
        register_data = {
            "clientId": self.client_id,
            "publicKey": self._get_public_key_pem(),
            "sharedToken": self.shared_token.decode('utf-8')
        }
        
        resp = self.session.post(
            f"{self.server_url}/api/keys/register",
            json=register_data
        )
        
        if resp.status_code != 200:
            print(f"[-] 注册失败: {resp.status_code}")
            return False
        
        data = resp.json()
        if not data.get('success'):
            print(f"[-] 注册失败: {data}")
            return False
        
        print(f"[+] 客户端注册成功")
        return True
    
    def _rsa_encrypt(self, data: bytes) -> bytes:
        """使用服务器公钥加密"""
        return self.server_public_key.encrypt(
            data,
            padding.OAEP(
                mgf=padding.MGF1(algorithm=hashes.SHA256()),
                algorithm=hashes.SHA256(),
                label=None
            )
        )
    
    def _rsa_decrypt(self, data: bytes) -> bytes:
        """使用客户端私钥解密"""
        return self.private_key.decrypt(
            data,
            padding.OAEP(
                mgf=padding.MGF1(algorithm=hashes.SHA256()),
                algorithm=hashes.SHA256(),
                label=None
            )
        )
    
    def _compute_hmac(self, data: bytes) -> bytes:
        """计算HMAC-SHA256"""
        h = hmac.HMAC(self.shared_token, hashes.SHA256(), backend=default_backend())
        h.update(data)
        return h.finalize()
    
    def _aes_decrypt(self, key: bytes, ciphertext: bytes) -> bytes:
        """AES-256-CBC解密"""
        iv = ciphertext[:16]
        actual_ciphertext = ciphertext[16:]
        
        cipher = Cipher(algorithms.AES(key), modes.CBC(iv), backend=default_backend())
        decryptor = cipher.decryptor()
        plaintext = decryptor.update(actual_ciphertext) + decryptor.finalize()
        
        padding_len = plaintext[-1]
        return plaintext[:-padding_len]
    
    def authenticate(self, target_path: str = "") -> bool:
        """执行Secure验证并获取会话ID"""
        print(f"[*] 正在执行Secure验证，目标路径: {target_path or '/'}")
        
        nonce = os.urandom(16)
        timestamp = int(time.time())
        timestamp_bytes = struct.pack('>q', timestamp)
        
        version = bytes([0x01])
        
        version_nonce_timestamp = version + nonce + timestamp_bytes
        token_hash = self._compute_hmac(version_nonce_timestamp)
        
        request_packet = version_nonce_timestamp + token_hash
        
        print(f"[DEBUG] 时间戳: {timestamp}")
        print(f"[DEBUG] Nonce: {nonce.hex()}")
        print(f"[DEBUG] Token hash: {token_hash.hex()}")
        print(f"[DEBUG] 请求包长度: {len(request_packet)}")
        
        encrypted_request = self._rsa_encrypt(request_packet)
        
        verify_data = {
            "clientId": self.client_id,
            "encryptedRequest": base64.b64encode(encrypted_request).decode('utf-8'),
            "path": target_path
        }
        
        resp = self.session.post(
            f"{self.server_url}/api/keys/verify",
            json=verify_data
        )
        
        if resp.status_code != 200:
            print(f"[-] 验证请求失败: {resp.status_code}")
            print(f"[-] 响应: {resp.text}")
            return False
        
        data = resp.json()
        if not data.get('success'):
            print(f"[-] 验证失败")
            return False
        
        self.session_id = data.get('sessionId')
        if not self.session_id:
            print(f"[-] 未获取到会话ID")
            return False
        
        response_packet = base64.b64decode(data['response'])
        
        offset = 0
        enc_session_key_len = struct.unpack('>I', response_packet[offset:offset+4])[0]
        offset += 4
        enc_session_key = response_packet[offset:offset+enc_session_key_len]
        offset += enc_session_key_len
        
        sig_len = struct.unpack('>I', response_packet[offset:offset+4])[0]
        offset += 4
        signature = response_packet[offset:offset+sig_len]
        offset += sig_len
        
        enc_response_data = response_packet[offset:]
        
        session_key = self._rsa_decrypt(enc_session_key)
        response_data = self._aes_decrypt(session_key, enc_response_data)
        
        print(f"[+] 验证成功，会话已建立")
        print(f"[+] 会话ID: {self.session_id[:16]}...")
        print(f"[+] 服务器响应: {response_data.decode('utf-8')}")
        
        return True
    
    def list_directory(self, path: str = "") -> dict:
        """列出目录内容"""
        url = f"{self.server_url}/api/files"
        params = {"path": path} if path else {}
        
        if self.session_id:
            params["session"] = self.session_id
        
        resp = self.session.get(url, params=params)
        
        if resp.status_code == 404:
            print(f"[-] 目录不存在或无权访问")
            return None
        
        if resp.status_code != 200:
            print(f"[-] 请求失败: {resp.status_code}")
            return None
        
        return resp.json()
    
    def print_directory(self, listing: dict, indent: int = 0):
        """格式化打印目录内容"""
        if not listing:
            return
        
        prefix = "  " * indent
        
        print(f"\n{prefix}当前路径: {listing.get('currentPath', '/') or '/'}")
        
        directories = listing.get('directories', [])
        if directories:
            print(f"{prefix}目录:")
            for d in directories:
                print(f"{prefix}  [DIR] {d['name']}")
                print(f"{prefix}        修改时间: {d['lastModified']}")
        
        files = listing.get('files', [])
        if files:
            print(f"{prefix}文件:")
            for f in files:
                print(f"{prefix}  [FILE] {f['name']}")
                print(f"{prefix}         大小: {f.get('size', 'N/A')}")
                print(f"{prefix}         修改时间: {f.get('lastModified', 'N/A')}")
                flags = []
                if f.get('canPreview'):
                    flags.append("可预览")
                if f.get('canDownload'):
                    flags.append("可下载")
                if flags:
                    print(f"{prefix}         [{', '.join(flags)}]")
        
        if not directories and not files:
            print(f"{prefix}(空目录)")


def main():
    parser = argparse.ArgumentParser(description='Repository Secure Client')
    parser.add_argument('--url', '-u', required=True, help='服务器URL')
    parser.add_argument('--client-id', '-c', required=True, help='客户端ID')
    parser.add_argument('--token', '-t', required=True, help='共享令牌')
    parser.add_argument('--path', '-p', default='', help='目标路径')
    parser.add_argument('--verify-ssl', action='store_true', help='验证SSL证书')
    
    args = parser.parse_args()
    
    print("=" * 60)
    print("Repository Secure Client")
    print("=" * 60)
    
    if not args.verify_ssl:
        print("[!] SSL证书验证已禁用")
    
    client = SecureClient(args.url, args.client_id, args.token, args.verify_ssl)
    
    if not client.register():
        print("\n[-] 注册失败，请检查配置")
        return
    
    if not client.authenticate(args.path):
        print("\n[-] 验证失败，请检查shared_token是否正确")
        return
    
    print("\n" + "=" * 60)
    print("正在获取目录列表...")
    print("=" * 60)
    
    listing = client.list_directory(args.path)
    if listing:
        client.print_directory(listing)
    else:
        print("[-] 无法获取目录内容")


if __name__ == "__main__":
    main()
