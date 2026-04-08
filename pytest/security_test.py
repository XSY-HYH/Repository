#!/usr/bin/env python3
"""
Repository Security Test Suite
测试Secure模式的各种攻击向量
"""

import requests
import json
import time
import os
import base64
import struct
import hashlib
import urllib.parse
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.backends import default_backend
from cryptography.hazmat.primitives import hmac
import warnings

warnings.filterwarnings('ignore')

SERVER_URL = "https://repo.ddns.net:59113"
PROTECTED_PATH = "UPDATA"
VALID_CLIENT_ID = "test_client"
VALID_TOKEN = "test_secret_token_123"

class SecurityTester:
    def __init__(self):
        self.session = requests.Session()
        self.session.verify = False
        self.vulnerabilities = []
        self.test_results = []
        
    def log_result(self, test_name: str, success: bool, message: str, is_vuln: bool = False):
        status = "[VULN]" if is_vuln else ("[PASS]" if success else "[FAIL]")
        result = f"{status} {test_name}: {message}"
        print(result)
        self.test_results.append({
            "test": test_name,
            "success": success,
            "message": message,
            "is_vulnerability": is_vuln
        })
        if is_vuln:
            self.vulnerabilities.append({
                "test": test_name,
                "message": message
            })
    
    def test_direct_access(self):
        """测试1: 直接访问受保护目录（无认证）"""
        print("\n" + "="*60)
        print("测试1: 直接访问受保护目录（无认证）")
        print("="*60)
        
        urls = [
            f"{SERVER_URL}/{PROTECTED_PATH}",
            f"{SERVER_URL}/{PROTECTED_PATH}/",
            f"{SERVER_URL}/api/files?path={PROTECTED_PATH}",
            f"{SERVER_URL}/api/files?path={PROTECTED_PATH}/",
        ]
        
        for url in urls:
            try:
                resp = self.session.get(url)
                if resp.status_code == 200:
                    if "UPDATA" in resp.text or "directories" in resp.text:
                        self.log_result(f"直接访问 {url}", False, 
                            f"返回200，可能泄露内容", is_vuln=True)
                    else:
                        self.log_result(f"直接访问 {url}", True, 
                            f"返回200但无敏感内容")
                elif resp.status_code == 404:
                    self.log_result(f"直接访问 {url}", True, 
                        f"正确返回404")
                else:
                    self.log_result(f"直接访问 {url}", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"直接访问 {url}", False, f"错误: {e}")
    
    def test_wrong_token(self):
        """测试2: 使用错误的token"""
        print("\n" + "="*60)
        print("测试2: 使用错误的共享令牌")
        print("="*60)
        
        wrong_tokens = [
            "wrong_token",
            "",
            "test_secret_token_1234",
            "TEST_SECRET_TOKEN_123",
            "test_secret_token_12",
        ]
        
        for token in wrong_tokens:
            try:
                client = SecureClient(SERVER_URL, VALID_CLIENT_ID, token if token else "dummy")
                result = client.authenticate(PROTECTED_PATH)
                if result:
                    self.log_result(f"错误token '{token[:20]}...'", False, 
                        "认证成功！严重漏洞！", is_vuln=True)
                else:
                    self.log_result(f"错误token '{token[:20]}...'", True, 
                        "认证被拒绝")
            except Exception as e:
                self.log_result(f"错误token '{token[:20]}...'", True, 
                    f"认证失败: {str(e)[:50]}")
    
    def test_wrong_client_id(self):
        """测试3: 使用未注册的client_id"""
        print("\n" + "="*60)
        print("测试3: 使用未注册的客户端ID")
        print("="*60)
        
        wrong_ids = [
            "attacker",
            "hacker",
            "",
            "test_client_attacker",
            "admin",
            "root",
        ]
        
        for client_id in wrong_ids:
            try:
                client = SecureClient(SERVER_URL, client_id if client_id else "dummy", VALID_TOKEN)
                result = client.authenticate(PROTECTED_PATH)
                if result:
                    self.log_result(f"错误client_id '{client_id}'", False, 
                        "认证成功！严重漏洞！", is_vuln=True)
                else:
                    self.log_result(f"错误client_id '{client_id}'", True, 
                        "认证被拒绝")
            except Exception as e:
                self.log_result(f"错误client_id '{client_id}'", True, 
                    f"认证失败: {str(e)[:50]}")
    
    def test_path_traversal(self):
        """测试4: 路径遍历攻击"""
        print("\n" + "="*60)
        print("测试4: 路径遍历攻击")
        print("="*60)
        
        traversal_paths = [
            "../",
            "..\\",
            "UPDATA/../",
            "UPDATA/..\\",
            "../public",
            "..\\public",
            "UPDATA/../../",
            "./UPDATA",
            "UPDATA/.",
            "UPDATA/./",
            "//UPDATA",
            "/UPDATA",
            "UPDATA%00",
            "UPDATA%2e%2e%2f",
            "..%2f",
            "%2e%2e/",
            "....//",
            "....//....//",
        ]
        
        client = SecureClient(SERVER_URL, VALID_CLIENT_ID, VALID_TOKEN)
        client.authenticate(PROTECTED_PATH)
        
        for path in traversal_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={urllib.parse.quote(path, safe='')}"
                resp = self.session.get(url, cookies={"session": client.session_id} if client.session_id else {})
                
                if resp.status_code == 200:
                    try:
                        data = resp.json()
                        if data.get("directories") or data.get("files"):
                            self.log_result(f"路径遍历 '{path}'", False, 
                                f"成功访问: {data}", is_vuln=True)
                        else:
                            self.log_result(f"路径遍历 '{path}'", True, 
                                "返回200但无内容")
                    except:
                        self.log_result(f"路径遍历 '{path}'", True, 
                            "返回200但非JSON")
                elif resp.status_code == 404:
                    self.log_result(f"路径遍历 '{path}'", True, 
                        "正确返回404")
                else:
                    self.log_result(f"路径遍历 '{path}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"路径遍历 '{path}'", False, f"错误: {e}")
    
    def test_session_hijacking(self):
        """测试5: 会话劫持"""
        print("\n" + "="*60)
        print("测试5: 会话劫持（使用他人的session）")
        print("="*60)
        
        valid_client = SecureClient(SERVER_URL, VALID_CLIENT_ID, VALID_TOKEN)
        valid_client.authenticate(PROTECTED_PATH)
        
        if valid_client.session_id:
            attacker_session = requests.Session()
            attacker_session.verify = False
            attacker_session.cookies.set("session", valid_client.session_id)
            
            try:
                resp = attacker_session.get(f"{SERVER_URL}/api/files?path={PROTECTED_PATH}")
                if resp.status_code == 200:
                    try:
                        data = resp.json()
                        if data.get("directories") is not None or data.get("files") is not None:
                            self.log_result("会话劫持", False, 
                                "使用他人session成功访问！", is_vuln=True)
                        else:
                            self.log_result("会话劫持", True, 
                                "session无效或已过期")
                    except:
                        self.log_result("会话劫持", True, 
                            "返回200但无有效数据")
                else:
                    self.log_result("会话劫持", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result("会话劫持", False, f"错误: {e}")
        else:
            self.log_result("会话劫持", True, "无法获取有效session")
    
    def test_replay_attack(self):
        """测试6: 重放攻击"""
        print("\n" + "="*60)
        print("测试6: 重放攻击")
        print("="*60)
        
        client = SecureClient(SERVER_URL, VALID_CLIENT_ID, VALID_TOKEN)
        
        auth_packet = client._build_auth_packet(PROTECTED_PATH)
        
        try:
            resp1 = self.session.post(
                f"{SERVER_URL}/api/secure/auth",
                json={"client_id": VALID_CLIENT_ID, "auth_packet": base64.b64encode(auth_packet).decode()}
            )
            
            if resp1.status_code == 200:
                print(f"[*] 第一次认证成功")
                
                time.sleep(1)
                
                resp2 = self.session.post(
                    f"{SERVER_URL}/api/secure/auth",
                    json={"client_id": VALID_CLIENT_ID, "auth_packet": base64.b64encode(auth_packet).decode()}
                )
                
                if resp2.status_code == 200:
                    self.log_result("重放攻击", False, 
                        "相同的认证包被接受两次！", is_vuln=True)
                else:
                    self.log_result("重放攻击", True, 
                        f"重放被拒绝，返回{resp2.status_code}")
            else:
                self.log_result("重放攻击", True, 
                    f"初始认证失败，返回{resp1.status_code}")
        except Exception as e:
            self.log_result("重放攻击", False, f"错误: {e}")
    
    def test_timestamp_manipulation(self):
        """测试7: 时间戳篡改"""
        print("\n" + "="*60)
        print("测试7: 时间戳篡改")
        print("="*60)
        
        timestamps = [
            0,
            1,
            9999999999,
            int(time.time()) - 3600,
            int(time.time()) - 86400,
            int(time.time()) + 3600,
            int(time.time()) + 86400,
        ]
        
        for ts in timestamps:
            try:
                client = SecureClient(SERVER_URL, VALID_CLIENT_ID, VALID_TOKEN)
                
                auth_packet = client._build_auth_packet_with_timestamp(PROTECTED_PATH, ts)
                
                resp = self.session.post(
                    f"{SERVER_URL}/api/secure/auth",
                    json={"client_id": VALID_CLIENT_ID, "auth_packet": base64.b64encode(auth_packet).decode()}
                )
                
                if resp.status_code == 200:
                    self.log_result(f"时间戳篡改 ts={ts}", False, 
                        "篡改的时间戳被接受！", is_vuln=True)
                else:
                    self.log_result(f"时间戳篡改 ts={ts}", True, 
                        f"被拒绝，返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"时间戳篡改 ts={ts}", True, 
                    f"被拒绝: {str(e)[:50]}")
    
    def test_file_access(self):
        """测试8: 直接文件访问"""
        print("\n" + "="*60)
        print("测试8: 直接文件访问（无认证）")
        print("="*60)
        
        test_files = [
            f"{PROTECTED_PATH}/test.txt",
            f"{PROTECTED_PATH}/../.keys/server_private.pem",
            f"{PROTECTED_PATH}/../.keys/server_public.pem",
            ".keys/server_private.pem",
            ".keys/server_public.pem",
        ]
        
        for file_path in test_files:
            try:
                url = f"{SERVER_URL}/api/download/{file_path}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    content_preview = resp.content[:100]
                    self.log_result(f"文件访问 '{file_path}'", False, 
                        f"成功下载！内容预览: {content_preview}", is_vuln=True)
                elif resp.status_code == 404:
                    self.log_result(f"文件访问 '{file_path}'", True, 
                        "正确返回404")
                else:
                    self.log_result(f"文件访问 '{file_path}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"文件访问 '{file_path}'", False, f"错误: {e}")
    
    def test_preview_access(self):
        """测试9: 文件预览访问"""
        print("\n" + "="*60)
        print("测试9: 文件预览访问（无认证）")
        print("="*60)
        
        test_files = [
            f"{PROTECTED_PATH}/test.txt",
            f"{PROTECTED_PATH}/../.keys/server_private.pem",
            ".keys/server_public.pem",
        ]
        
        for file_path in test_files:
            try:
                url = f"{SERVER_URL}/api/preview/{file_path}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    self.log_result(f"预览访问 '{file_path}'", False, 
                        f"成功预览！", is_vuln=True)
                elif resp.status_code == 404:
                    self.log_result(f"预览访问 '{file_path}'", True, 
                        "正确返回404")
                else:
                    self.log_result(f"预览访问 '{file_path}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"预览访问 '{file_path}'", False, f"错误: {e}")
    
    def test_upload_access(self):
        """测试10: 上传到受保护目录"""
        print("\n" + "="*60)
        print("测试10: 上传到受保护目录（无认证）")
        print("="*60)
        
        test_paths = [
            PROTECTED_PATH,
            f"{PROTECTED_PATH}/subdir",
            ".keys",
            "",
        ]
        
        for path in test_paths:
            try:
                files = {'file': ('test_upload.txt', b'test content')}
                url = f"{SERVER_URL}/api/upload/{path}"
                resp = self.session.post(url, files=files)
                
                if resp.status_code == 200:
                    self.log_result(f"上传到 '{path}'", False, 
                        f"上传成功！响应: {resp.text[:100]}", is_vuln=True)
                elif resp.status_code == 404:
                    self.log_result(f"上传到 '{path}'", True, 
                        "正确返回404")
                elif resp.status_code == 403:
                    self.log_result(f"上传到 '{path}'", True, 
                        "正确返回403（上传禁用）")
                else:
                    self.log_result(f"上传到 '{path}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"上传到 '{path}'", False, f"错误: {e}")
    
    def test_url_encoding_bypass(self):
        """测试11: URL编码绕过"""
        print("\n" + "="*60)
        print("测试11: URL编码绕过")
        print("="*60)
        
        encoded_paths = [
            "%55PDATA",
            "UP%44ATA",
            "UPDATA%00",
            "UPDATA%2f",
            "UPDATA%5c",
            "%2e%2e%2fUPDATA",
            "..%252fUPDATA",
            "UPDATA%252f..%252f",
        ]
        
        for path in encoded_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={path}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    try:
                        data = resp.json()
                        if data.get("directories") or data.get("files"):
                            self.log_result(f"编码绕过 '{path}'", False, 
                                f"成功访问: {data}", is_vuln=True)
                        else:
                            self.log_result(f"编码绕过 '{path}'", True, 
                                "返回200但无内容")
                    except:
                        self.log_result(f"编码绕过 '{path}'", True, 
                            "返回200但非JSON")
                elif resp.status_code == 404:
                    self.log_result(f"编码绕过 '{path}'", True, 
                        "正确返回404")
                else:
                    self.log_result(f"编码绕过 '{path}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"编码绕过 '{path}'", False, f"错误: {e}")
    
    def test_case_manipulation(self):
        """测试12: 大小写混淆"""
        print("\n" + "="*60)
        print("测试12: 大小写混淆")
        print("="*60)
        
        case_paths = [
            "updata",
            "UpDaTa",
            "upDATA",
            "UPDATA",
            "Updata",
        ]
        
        for path in case_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={path}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    try:
                        data = resp.json()
                        if data.get("directories") or data.get("files"):
                            self.log_result(f"大小写混淆 '{path}'", False, 
                                f"成功访问！", is_vuln=True)
                        else:
                            self.log_result(f"大小写混淆 '{path}'", True, 
                                "返回200但无内容")
                    except:
                        self.log_result(f"大小写混淆 '{path}'", True, 
                            "返回200但非JSON")
                elif resp.status_code == 404:
                    self.log_result(f"大小写混淆 '{path}'", True, 
                        "正确返回404")
                else:
                    self.log_result(f"大小写混淆 '{path}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"大小写混淆 '{path}'", False, f"错误: {e}")
    
    def test_special_characters(self):
        """测试13: 特殊字符注入"""
        print("\n" + "="*60)
        print("测试13: 特殊字符注入")
        print("="*60)
        
        special_paths = [
            "UPDATA'",
            'UPDATA"',
            "UPDATA; DROP TABLE--",
            "UPDATA<script>alert(1)</script>",
            "UPDATA{{template}}",
            "UPDATA${env}",
            "UPDATA%0d%0a",
            "UPDATA\t",
            "UPDATA\n",
        ]
        
        for path in special_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={urllib.parse.quote(path)}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    self.log_result(f"特殊字符 '{path[:20]}'", True, 
                        "返回200（可能是错误页面）")
                elif resp.status_code == 404:
                    self.log_result(f"特殊字符 '{path[:20]}'", True, 
                        "正确返回404")
                elif resp.status_code == 400:
                    self.log_result(f"特殊字符 '{path[:20]}'", True, 
                        "正确返回400（非法字符）")
                else:
                    self.log_result(f"特殊字符 '{path[:20]}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"特殊字符 '{path[:20]}'", False, f"错误: {e}")
    
    def test_keys_directory_access(self):
        """测试14: .keys目录访问（HRVU-202631204验证）"""
        print("\n" + "="*60)
        print("测试14: .keys目录访问验证")
        print("="*60)
        
        keys_paths = [
            ".keys",
            ".keys/",
            ".keys/server_private.pem",
            ".keys/server_public.pem",
            "./.keys",
            "../.keys",
            "UPDATA/../.keys",
        ]
        
        for path in keys_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={path}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    self.log_result(f".keys访问 '{path}'", False, 
                        f"成功访问！漏洞未修复！", is_vuln=True)
                elif resp.status_code == 404:
                    self.log_result(f".keys访问 '{path}'", True, 
                        "正确返回404（已修复）")
                else:
                    self.log_result(f".keys访问 '{path}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f".keys访问 '{path}'", False, f"错误: {e}")
    
    def test_http_method_tampering(self):
        """测试15: HTTP方法篡改"""
        print("\n" + "="*60)
        print("测试15: HTTP方法篡改")
        print("="*60)
        
        methods = ['PUT', 'DELETE', 'PATCH', 'OPTIONS', 'HEAD', 'TRACE', 'CONNECT']
        
        for method in methods:
            try:
                url = f"{SERVER_URL}/api/files?path={PROTECTED_PATH}"
                resp = self.session.request(method, url)
                
                if resp.status_code == 200 and method not in ['HEAD', 'OPTIONS']:
                    self.log_result(f"HTTP方法 '{method}'", False, 
                        f"意外成功！状态码: {resp.status_code}", is_vuln=True)
                else:
                    self.log_result(f"HTTP方法 '{method}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"HTTP方法 '{method}'", True, f"被拒绝: {str(e)[:50]}")
    
    def test_unicode_bypass(self):
        """测试16: Unicode编码绕过"""
        print("\n" + "="*60)
        print("测试16: Unicode编码绕过")
        print("="*60)
        
        unicode_paths = [
            "UPDATA%u002f..",
            "UPDATA%uff0f..",
            "%uff0e%uff0e%uff0f",
            "UPDATA%u0000",
            "UPDATA%uff1a",  # 全角冒号
            "\uff21\uff30\uff24\uff21\uff34\uff21",  # 全角UPDATA
            "UPDATA%u202e",  # RTL override
            "UPDATA%u200b",  # Zero width space
            "UPDATA%u200c",  # Zero width non-joiner
            "UPDATA%u200d",  # Zero width joiner
        ]
        
        for path in unicode_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={path}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    try:
                        data = resp.json()
                        if data.get("directories") or data.get("files"):
                            self.log_result(f"Unicode绕过 '{path[:30]}'", False, 
                                f"成功访问！", is_vuln=True)
                        else:
                            self.log_result(f"Unicode绕过 '{path[:30]}'", True, 
                                "返回200但无内容")
                    except:
                        self.log_result(f"Unicode绕过 '{path[:30]}'", True, 
                            "返回200但非JSON")
                else:
                    self.log_result(f"Unicode绕过 '{path[:30]}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"Unicode绕过 '{path[:30]}'", True, f"错误: {str(e)[:50]}")
    
    def test_double_extension_attack(self):
        """测试17: 双重扩展名攻击"""
        print("\n" + "="*60)
        print("测试17: 双重扩展名攻击")
        print("="*60)
        
        malicious_filenames = [
            "test.txt.php",
            "test.txt.aspx",
            "test.txt.jsp",
            "test.txt.exe",
            "test.txt.bat",
            "test.txt.cmd",
            "test.txt.sh",
            "test.jpg.php",
            "test.png.aspx",
            "test.html.txt",
        ]
        
        for filename in malicious_filenames:
            try:
                files = {'file': (filename, b'test malicious content')}
                url = f"{SERVER_URL}/api/upload/public"
                resp = self.session.post(url, files=files)
                
                if resp.status_code == 200:
                    self.log_result(f"双重扩展名 '{filename}'", False, 
                        f"上传成功！可能存在执行漏洞", is_vuln=True)
                elif resp.status_code == 403:
                    self.log_result(f"双重扩展名 '{filename}'", True, 
                        "被正确拒绝（上传禁用或扩展名限制）")
                else:
                    self.log_result(f"双重扩展名 '{filename}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"双重扩展名 '{filename}'", False, f"错误: {e}")
    
    def test_null_byte_injection(self):
        """测试18: 空字节注入"""
        print("\n" + "="*60)
        print("测试18: 空字节注入")
        print("="*60)
        
        null_paths = [
            "UPDATA%00.txt",
            "UPDATA%00.jpg",
            "UPDATA%00.html",
            "UPDATA/test%00.txt",
            "%00UPDATA",
            "UPDATA%00/../../",
        ]
        
        for path in null_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={path}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    self.log_result(f"空字节注入 '{path}'", False, 
                        f"可能存在漏洞", is_vuln=True)
                else:
                    self.log_result(f"空字节注入 '{path}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"空字节注入 '{path}'", True, f"错误: {str(e)[:50]}")
    
    def test_crlf_injection(self):
        """测试19: CRLF注入"""
        print("\n" + "="*60)
        print("测试19: CRLF注入")
        print("="*60)
        
        crlf_paths = [
            "UPDATA%0d%0aSet-Cookie: hacked=true",
            "UPDATA%0aSet-Cookie: hacked=true",
            "UPDATA%0dSet-Cookie: hacked=true",
            "UPDATA%0d%0a%0d%0a<html>hacked</html>",
            "UPDATA%0d%0aLocation: http://evil.com",
        ]
        
        for path in crlf_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={path}"
                resp = self.session.get(url)
                
                if 'hacked' in resp.headers.get('Set-Cookie', ''):
                    self.log_result(f"CRLF注入 '{path[:40]}'", False, 
                        f"注入成功！Cookie被设置", is_vuln=True)
                elif 'hacked' in resp.text:
                    self.log_result(f"CRLF注入 '{path[:40]}'", False, 
                        f"响应体被污染", is_vuln=True)
                else:
                    self.log_result(f"CRLF注入 '{path[:40]}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"CRLF注入 '{path[:40]}'", True, f"错误: {str(e)[:50]}")
    
    def test_parameter_pollution(self):
        """测试20: 参数污染攻击"""
        print("\n" + "="*60)
        print("测试20: 参数污染攻击")
        print("="*60)
        
        pollution_urls = [
            f"{SERVER_URL}/api/files?path=public&path={PROTECTED_PATH}",
            f"{SERVER_URL}/api/files?path[]={PROTECTED_PATH}",
            f"{SERVER_URL}/api/files?path[0]={PROTECTED_PATH}",
            f"{SERVER_URL}/api/files?path=public&path=../../etc/passwd",
            f"{SERVER_URL}/api/files?path=UPDATA&token=&token=admin",
        ]
        
        for url in pollution_urls:
            try:
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    try:
                        data = resp.json()
                        if PROTECTED_PATH in str(data) and "directories" in str(data):
                            self.log_result(f"参数污染 '{url.split('?')[1][:40]}'", False, 
                                f"成功绕过！", is_vuln=True)
                        else:
                            self.log_result(f"参数污染 '{url.split('?')[1][:40]}'", True, 
                                "返回200但无敏感数据")
                    except:
                        self.log_result(f"参数污染 '{url.split('?')[1][:40]}'", True, 
                            "返回200但非JSON")
                elif resp.status_code == 400:
                    self.log_result(f"参数污染 '{url.split('?')[1][:40]}'", True, 
                        "正确返回400（参数错误）")
                else:
                    self.log_result(f"参数污染 '{url.split('?')[1][:40]}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"参数污染", True, f"错误: {str(e)[:50]}")
    
    def test_backup_file_access(self):
        """测试21: 备份文件访问"""
        print("\n" + "="*60)
        print("测试21: 备份文件访问")
        print("="*60)
        
        backup_files = [
            "Config.json.bak",
            "Config.json~",
            "Config.json.backup",
            "Config.json.old",
            "Config.json.swp",
            "Config.json.swo",
            ".Config.json.swp",
            "Protectionlock.json.bak",
            "Protectionlock.json~",
            "web.config.bak",
            "web.config~",
            ".htaccess.bak",
            ".htaccess~",
        ]
        
        for file in backup_files:
            try:
                url = f"{SERVER_URL}/api/download/{file}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    self.log_result(f"备份文件 '{file}'", False, 
                        f"成功下载备份文件！", is_vuln=True)
                elif resp.status_code == 404:
                    self.log_result(f"备份文件 '{file}'", True, 
                        "正确返回404")
                else:
                    self.log_result(f"备份文件 '{file}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"备份文件 '{file}'", False, f"错误: {e}")
    
    def test_sensitive_file_access(self):
        """测试22: 敏感文件访问"""
        print("\n" + "="*60)
        print("测试22: 敏感文件访问")
        print("="*60)
        
        sensitive_files = [
            "Config.json",
            "data.ini",
            "Protectionlock.json",
            ".env",
            "config.json",
            "settings.json",
            "secrets.json",
            "credentials.json",
            "id_rsa",
            "id_rsa.pub",
            ".git/config",
            ".git/HEAD",
            ".svn/entries",
            "web.config",
            ".htaccess",
            "error.log",
            "access.log",
        ]
        
        for file in sensitive_files:
            try:
                url = f"{SERVER_URL}/api/download/{file}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    content_preview = resp.text[:100] if resp.text else "(binary)"
                    self.log_result(f"敏感文件 '{file}'", False, 
                        f"成功下载！内容: {content_preview}", is_vuln=True)
                elif resp.status_code == 404:
                    self.log_result(f"敏感文件 '{file}'", True, 
                        "正确返回404")
                else:
                    self.log_result(f"敏感文件 '{file}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"敏感文件 '{file}'", False, f"错误: {e}")
    
    def test_long_path_attack(self):
        """测试23: 超长路径攻击"""
        print("\n" + "="*60)
        print("测试23: 超长路径攻击")
        print("="*60)
        
        long_paths = [
            "A" * 1000,
            "A" * 5000,
            "A/" * 100,
            "../" * 100,
            "UPDATA/" + "A" * 500,
        ]
        
        for path in long_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={path[:100]}"
                resp = self.session.get(url, timeout=5)
                
                if resp.status_code == 500:
                    self.log_result(f"超长路径 (长度{len(path)})", False, 
                        f"服务器错误！可能存在缓冲区溢出", is_vuln=True)
                elif resp.status_code == 414:
                    self.log_result(f"超长路径 (长度{len(path)})", True, 
                        "正确返回414（URI过长）")
                elif resp.status_code == 400:
                    self.log_result(f"超长路径 (长度{len(path)})", True, 
                        "正确返回400")
                else:
                    self.log_result(f"超长路径 (长度{len(path)})", True, 
                        f"返回{resp.status_code}")
            except requests.exceptions.Timeout:
                self.log_result(f"超长路径 (长度{len(path)})", False, 
                    "请求超时！可能存在DoS漏洞", is_vuln=True)
            except Exception as e:
                self.log_result(f"超长路径 (长度{len(path)})", True, f"错误: {str(e)[:50]}")
    
    def test_malicious_filename(self):
        """测试24: 恶意文件名"""
        print("\n" + "="*60)
        print("测试24: 恶意文件名")
        print("="*60)
        
        malicious_names = [
            "../../../etc/passwd",
            "..\\..\\..\\windows\\system32\\config\\sam",
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "LPT1",
            "test<script>alert(1)</script>.txt",
            "test\".txt",
            "test'.txt",
            "test|.txt",
            "test\x00.txt",
            "test\t.txt",
            "test\n.txt",
            "test\r\n.txt",
        ]
        
        for filename in malicious_names:
            try:
                files = {'file': (filename, b'test content')}
                url = f"{SERVER_URL}/api/upload/public"
                resp = self.session.post(url, files=files)
                
                if resp.status_code == 200:
                    self.log_result(f"恶意文件名 '{filename[:30]}'", False, 
                        f"上传成功！", is_vuln=True)
                elif resp.status_code == 400:
                    self.log_result(f"恶意文件名 '{filename[:30]}'", True, 
                        "正确返回400（非法文件名）")
                else:
                    self.log_result(f"恶意文件名 '{filename[:30]}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"恶意文件名 '{filename[:30]}'", True, f"错误: {str(e)[:50]}")
    
    def test_content_type_bypass(self):
        """测试25: Content-Type绕过"""
        print("\n" + "="*60)
        print("测试25: Content-Type绕过")
        print("="*60)
        
        content_types = [
            ("image/jpeg", "test.php"),
            ("image/png", "test.aspx"),
            ("application/pdf", "test.jsp"),
            ("text/plain", "test.exe"),
            ("application/octet-stream", "test.php"),
            ("image/gif", "test.php"),
        ]
        
        for content_type, filename in content_types:
            try:
                files = {'file': (filename, b'<?php system($_GET["cmd"]); ?>', content_type)}
                url = f"{SERVER_URL}/api/upload/public"
                resp = self.session.post(url, files=files)
                
                if resp.status_code == 200:
                    self.log_result(f"Content-Type '{content_type}' + '{filename}'", False, 
                        f"上传成功！可能绕过扩展名检查", is_vuln=True)
                elif resp.status_code == 403:
                    self.log_result(f"Content-Type '{content_type}' + '{filename}'", True, 
                        "被正确拒绝")
                else:
                    self.log_result(f"Content-Type '{content_type}' + '{filename}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"Content-Type测试", True, f"错误: {str(e)[:50]}")
    
    def test_zip_slip_attack(self):
        """测试26: Zip Slip攻击"""
        print("\n" + "="*60)
        print("测试26: Zip Slip路径遍历")
        print("="*60)
        
        zip_slip_paths = [
            "../../../var/www/html/shell.php",
            "..\\..\\..\\inetpub\\wwwroot\\shell.asp",
            "....//....//....//etc/passwd",
            "..%5c..%5c..%5cetc/passwd",
            "%2e%2e%5c%2e%2e%5c%2e%2e%5cetc/passwd",
        ]
        
        for path in zip_slip_paths:
            try:
                url = f"{SERVER_URL}/api/files?path={urllib.parse.quote(path)}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    self.log_result(f"Zip Slip '{path[:40]}'", False, 
                        f"可能存在路径遍历", is_vuln=True)
                else:
                    self.log_result(f"Zip Slip '{path[:40]}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"Zip Slip '{path[:40]}'", True, f"错误: {str(e)[:50]}")
    
    def test_token_manipulation(self):
        """测试27: Token篡改"""
        print("\n" + "="*60)
        print("测试27: Token篡改攻击")
        print("="*60)
        
        fake_tokens = [
            "admin",
            "root",
            "administrator",
            "0000000000000000000000000000000000000000000000000000000000000000",
            "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
            "",
            "null",
            "undefined",
            "true",
            "false",
            "[]",
            "{}",
        ]
        
        for token in fake_tokens:
            try:
                url = f"{SERVER_URL}/api/files?path={PROTECTED_PATH}&token={urllib.parse.quote(token)}"
                resp = self.session.get(url)
                
                if resp.status_code == 200:
                    try:
                        data = resp.json()
                        if data.get("directories") is not None:
                            self.log_result(f"Token篡改 '{token[:20]}'", False, 
                                f"成功绕过认证！", is_vuln=True)
                        else:
                            self.log_result(f"Token篡改 '{token[:20]}'", True, 
                                "返回200但无数据")
                    except:
                        self.log_result(f"Token篡改 '{token[:20]}'", True, 
                            "返回200但非JSON")
                else:
                    self.log_result(f"Token篡改 '{token[:20]}'", True, 
                        f"返回{resp.status_code}")
            except Exception as e:
                self.log_result(f"Token篡改 '{token[:20]}'", True, f"错误: {str(e)[:50]}")
    
    def run_all_tests(self):
        """运行所有测试"""
        print("\n" + "="*60)
        print("Repository Security Test Suite (Extended)")
        print(f"目标: {SERVER_URL}")
        print(f"受保护目录: {PROTECTED_PATH}")
        print("="*60)
        
        self.test_direct_access()
        self.test_wrong_token()
        self.test_wrong_client_id()
        self.test_path_traversal()
        self.test_session_hijacking()
        self.test_replay_attack()
        self.test_timestamp_manipulation()
        self.test_file_access()
        self.test_preview_access()
        self.test_upload_access()
        self.test_url_encoding_bypass()
        self.test_case_manipulation()
        self.test_special_characters()
        self.test_keys_directory_access()
        self.test_http_method_tampering()
        self.test_unicode_bypass()
        self.test_double_extension_attack()
        self.test_null_byte_injection()
        self.test_crlf_injection()
        self.test_parameter_pollution()
        self.test_backup_file_access()
        self.test_sensitive_file_access()
        self.test_long_path_attack()
        self.test_malicious_filename()
        self.test_content_type_bypass()
        self.test_zip_slip_attack()
        self.test_token_manipulation()
        
        print("\n" + "="*60)
        print("测试结果汇总")
        print("="*60)
        
        total = len(self.test_results)
        passed = sum(1 for r in self.test_results if r["success"])
        vulns = len(self.vulnerabilities)
        
        print(f"总测试数: {total}")
        print(f"通过: {passed}")
        print(f"失败: {total - passed}")
        print(f"发现漏洞: {vulns}")
        
        if self.vulnerabilities:
            print("\n" + "!"*60)
            print("发现的安全漏洞:")
            print("!"*60)
            for i, vuln in enumerate(self.vulnerabilities, 1):
                print(f"\n漏洞 {i}: {vuln['test']}")
                print(f"  详情: {vuln['message']}")
        
        return self.vulnerabilities


class SecureClient:
    def __init__(self, server_url: str, client_id: str, shared_token: str):
        self.server_url = server_url.rstrip('/')
        self.client_id = client_id
        self.shared_token = shared_token.encode('utf-8')
        self.session = requests.Session()
        self.session.verify = False
        self.session_id = None
        
        self.private_key = rsa.generate_private_key(
            public_exponent=65537,
            key_size=2048,
            backend=default_backend()
        )
        self.public_key = self.private_key.public_key()
        
        self.server_public_key = None
    
    def _get_public_key_pem(self) -> str:
        return self.public_key.public_bytes(
            encoding=serialization.Encoding.PEM,
            format=serialization.PublicFormat.SubjectPublicKeyInfo
        ).decode('utf-8')
    
    def register(self) -> bool:
        try:
            resp = self.session.post(
                f"{self.server_url}/api/secure/register",
                json={
                    "client_id": self.client_id,
                    "public_key": self._get_public_key_pem()
                }
            )
            
            if resp.status_code == 200:
                data = resp.json()
                if data.get("status") == "success":
                    server_pubkey_pem = data.get("public_key")
                    self.server_public_key = serialization.load_pem_public_key(
                        server_pubkey_pem.encode('utf-8'),
                        backend=default_backend()
                    )
                    return True
            return False
        except:
            return False
    
    def _build_auth_packet(self, path: str) -> bytes:
        timestamp = int(time.time())
        return self._build_auth_packet_with_timestamp(path, timestamp)
    
    def _build_auth_packet_with_timestamp(self, path: str, timestamp: int) -> bytes:
        nonce = os.urandom(16)
        
        ts_bytes = struct.pack('>Q', timestamp)
        
        h = hmac.HMAC(self.shared_token, hashes.SHA256(), backend=default_backend())
        h.update(ts_bytes)
        h.update(nonce)
        h.update(path.encode('utf-8'))
        token_hash = h.finalize()
        
        packet = ts_bytes + nonce + token_hash
        return packet
    
    def authenticate(self, path: str) -> bool:
        if not self.register():
            return False
        
        auth_packet = self._build_auth_packet(path)
        
        try:
            resp = self.session.post(
                f"{self.server_url}/api/secure/auth",
                json={
                    "client_id": self.client_id,
                    "auth_packet": base64.b64encode(auth_packet).decode()
                }
            )
            
            if resp.status_code == 200:
                data = resp.json()
                if data.get("status") == "success":
                    encrypted_session = base64.b64decode(data.get("session", ""))
                    
                    session_key = self.shared_token.ljust(32, b'\0')[:32]
                    iv = encrypted_session[:16]
                    ciphertext = encrypted_session[16:]
                    
                    cipher = Cipher(algorithms.AES(session_key), modes.CBC(iv), backend=default_backend())
                    decryptor = cipher.decryptor()
                    decrypted = decryptor.update(ciphertext) + decryptor.finalize()
                    
                    self.session_id = decrypted.decode('utf-8').rstrip('\0')
                    return True
            return False
        except:
            return False


if __name__ == "__main__":
    tester = SecurityTester()
    vulnerabilities = tester.run_all_tests()
    
    if vulnerabilities:
        print("\n[!] 发现安全漏洞，建议创建HRVU文档记录")
    else:
        print("\n[+] 所有测试通过，未发现明显安全漏洞")
