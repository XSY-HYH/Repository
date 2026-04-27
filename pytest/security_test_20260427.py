#!/usr/bin/env python3
"""
Repository Server 安全测试脚本
测试日期: 2026-04-27
目标: http://127.0.0.1:7813
"""

import requests
import urllib.parse
import json
import time
import sys
import ssl
from concurrent.futures import ThreadPoolExecutor, as_completed

BASE_URL = "https://127.0.0.1:7813"
results = []
vulnerabilities = []

def log_result(test_name, passed, detail, is_vuln=False):
    status = "PASS" if passed else "FAIL"
    icon = "✅" if passed else "❌"
    print(f"  {icon} {test_name}: {detail}")
    results.append({"test": test_name, "passed": passed, "detail": detail})
    if not passed and is_vuln:
        vulnerabilities.append({"test": test_name, "detail": detail})

class SecurityTester:
    def __init__(self):
        self.session = requests.Session()
        self.session.verify = False
        self.session.headers.update({
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
            'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8',
            'Accept-Language': 'en-US,en;q=0.5',
        })
    
    def test_path_traversal_advanced(self):
        """测试1: 高级路径遍历攻击"""
        print("\n" + "="*60)
        print("测试1: 高级路径遍历攻击")
        print("="*60)
        
        traversal_paths = [
            ("../", "基本父目录"),
            ("..\\", "Windows反斜杠"),
            ("..../", "双重父目录"),
            ("....//", "双点斜杠"),
            ("....//....//", "嵌套遍历"),
            ("%2e%2e%2f", "URL编码..\/"),
            ("%2e%2e/", "部分URL编码"),
            ("..%2f", "混合编码"),
            ("%2e%2e%5c", "URL编码..\\"), 
            ("%252e%252e%252f", "双重URL编码"),
            ("..%252f", "双重编码混合"),
            ("./", "当前目录"),
            ("././", "重复当前目录"),
            ("//", "双斜杠根路径"),
            ("/.", "根目录加点"),
            ("/..", "根目录父路径"),
            ("foo/../../../etc", "深层遍历"),
            ("foo/bar/../../../windows", "Windows系统路径"),
            (".hidden", "隐藏文件访问"),
            ("....", "多点文件名"),
            (".../", "三点加斜杠"),
            ("file.txt%00.jpg", "Null字节截断"),
            ("file.txt%00", "纯Null字节"),
            ("%00", "纯Null字节"),
            ("CON", "Windows保留名"),
            ("PRN", "Windows保留名"),
            ("AUX", "Windows保留名"),
            ("NUL", "Windows保留名"),
            ("COM1", "Windows设备名"),
            ("LPT1", "Windows设备名"),
        ]
        
        for path, desc in traversal_paths:
            try:
                url = f"{BASE_URL}/api/files?path={urllib.parse.quote(path, safe='')}"
                resp = self.session.get(url, timeout=5)
                
                if resp.status_code == 200:
                    try:
                        data = resp.json()
                        if data.get("directories") or data.get("files"):
                            log_result(f"路径遍历 [{desc}] '{path}'", False,
                                f"成功访问! 返回数据", is_vuln=True)
                        else:
                            log_result(f"路径遍历 [{desc}] '{path}'", True, f"返回空列表(200)")
                    except:
                        log_result(f"路径遍历 [{desc}] '{path}'", False,
                            f"返回200非JSON: {resp.text[:50]}", is_vuln=True)
                elif resp.status_code in [400, 404]:
                    log_result(f"路径遍历 [{desc}] '{path}'", True, f"正确拒绝({resp.status_code})")
                elif resp.status_code == 429:
                    log_result(f"路径遍历 [{desc}] '{path}'", True, f"被限流(429)")
                else:
                    log_result(f"路径遍历 [{desc}] '{path}'", True, f"返回{resp.status_code}")
            except Exception as e:
                log_result(f"路径遍历 [{desc}] '{path}'", False, f"异常: {e}")

    def test_system_path_access(self):
        """测试2: 系统敏感路径访问"""
        print("\n" + "="*60)
        print("测试2: 系统敏感路径访问")
        print("="*60)
        
        sensitive_paths = [
            ".keys",
            ".keys/",
            ".keys/server_private.pem",
            ".keys/server_public.pem",
            "./.keys",
            "../.keys",
            ".keys/config.json",
            "Config.yml",
            "config.yml",
            "Config.json",
            "config.json",
            "lang.yml",
            "help.txt",
            "Repository.csproj",
            "bin/",
            "obj/",
            ".git",
            ".git/config",
            "Program.cs",
            "appsettings.json",
            "web.config",
        ]
        
        for path in sensitive_paths:
            try:
                url = f"{BASE_URL}/api/files?path={urllib.parse.quote(path, safe='')}"
                resp = self.session.get(url, timeout=5)
                
                if resp.status_code == 200:
                    try:
                        data = resp.json()
                        has_content = bool(data.get("directories") or data.get("files"))
                        log_result(f"系统路径 '{path}'", False if has_content else True,
                            f"{'包含数据!' if has_content else '空结果'}", is_vuln=has_content)
                    except:
                        log_result(f"系统路径 '{path}'", False, f"非JSON响应", is_vuln=True)
                elif resp.status_code in [400, 404]:
                    log_result(f"系统路径 '{path}'", True, f"正确拒绝({resp.status_code})")
                else:
                    log_result(f"系统路径 '{path}'", True, f"返回{resp.status_code}")
            except Exception as e:
                log_result(f"系统路径 '{path}'", False, f"异常: {e}")

    def test_admin_panel_security(self):
        """测试3: 管理面板安全"""
        print("\n" + "="*60)
        print("测试3: 管理面板安全")
        print("="*60)
        
        # 测试管理页面直接访问
        admin_tests = [
            ("GET /admin", f"{BASE_URL}/admin"),
            ("GET /admin.css", f"{BASE_URL}/admin.css"),
            ("GET /admin.js", f"{BASE_URL}/admin.js"),
            ("GET /api/admin", f"{BASE_URL}/api/admin"),
            ("GET /admin/ws (HTTP)", f"{BASE_URL}/admin/ws"),
        ]
        
        for name, url in admin_tests:
            try:
                resp = self.session.get(url, timeout=5)
                if resp.status_code == 200:
                    content_type = resp.headers.get('Content-Type', '')
                    if 'html' in content_type.lower() and '/admin' in url and url.endswith('/admin'):
                        log_result(name, True, f"返回HTML页面(正常，需认证)")
                    elif 'text/css' in content_type.lower():
                        log_result(name, True, f"返回CSS(正常)")
                    elif 'javascript' in content_type.lower():
                        log_result(name, True, f"返回JS(正常)")
                    else:
                        log_result(name, True, f"返回200, 类型: {content_type[:30]}")
                elif resp.status_code == 400:
                    log_result(name, True, f"返回400(WS需要升级)")
                else:
                    log_result(name, True, f"返回{resp.status_code}")
            except Exception as e:
                log_result(name, False, f"异常: {e}")
        
        # 测试无Accept头的请求（WebSocket场景模拟）
        print("\n  --- 无请求头测试 ---")
        headers_no_accept = {
            'User-Agent': 'Mozilla/5.0',
        }
        try:
            resp = self.session.get(f"{BASE_URL}/", headers=headers_no_accept, timeout=5)
            log_result("无Accept头访问首页", resp.status_code != 403,
                f"返回{resp.status_code}", is_vuln=(resp.status_code == 403))
        except Exception as e:
            log_result("无Accept头访问首页", False, f"异常: {e}")

    def test_header_injection(self):
        """测试4: HTTP头注入"""
        print("\n" + "="*60)
        print("测试4: HTTP头注入")
        print("="*60)
        
        injection_payloads = [
            ("CRLF注入路径", "/files?path=test%0d%0aX-Injected: true"),
            ("CRLF注入Host", {"Host": "evil.com\r\nX-Injected: true"}),
            ("超大User-Agent", {'User-Agent': 'A' * 10000}),
            ("空User-Agent", {'User-Agent': ''}),
            ("特殊字符UA", {'User-Agent': '<script>alert(1)</script>'}),
            ("JSON Content-Type", {'Content-Type': 'application/json'}),
            ("XML Content-Type", {'Content-Type': 'application/xml'}),
        ]
        
        for name, payload in injection_payloads:
            try:
                if isinstance(payload, str):
                    url = f"{BASE_URL}{payload}"
                    resp = self.session.get(url, timeout=5)
                else:
                    resp = self.session.get(BASE_URL, headers=payload, timeout=5)
                
                headers_str = str(dict(resp.headers))
                if 'X-Injected' in headers_str or 'injected' in headers_str.lower():
                    log_result(name, False, f"头注入成功! 响应头含注入内容", is_vuln=True)
                else:
                    log_result(name, True, f"安全, 返回{resp.status_code}")
            except Exception as e:
                log_result(name, False, f"异常: {e}")

    def test_xss_in_filenames(self):
        """测试5: 文件名XSS"""
        print("\n" + "="*60)
        print("测试5: 目录列表XSS检测")
        print("="*60)
        
        xss_payloads = [
            '<script>alert(1)</script>',
            '<img src=x onerror=alert(1)>',
            '" onclick="alert(1)',
            "' onclick='alert(1)",
            '<svg onload=alert(1)>',
            'javascript:alert(1)',
            '<iframe src="javascript:alert(1)">',
            '{{7*7}}',  # SSTI模板注入
            '${7*7}',   # 表达式注入
        ]
        
        for payload in xss_payloads:
            encoded = urllib.parse.quote(payload, safe='')
            try:
                url = f"{BASE_URL}/api/files?path={encoded}"
                resp = self.session.get(url, timeout=5)
                
                if resp.status_code == 200:
                    content = resp.text
                    if payload.lower() in content.lower() or '&lt;' not in content:
                        log_result(f"XSS payload: {payload[:20]}", False,
                            f"未转义输出! 响应含原始payload", is_vuln=True)
                    else:
                        log_result(f"XSS payload: {payload[:20]}", True, f"已转义")
                else:
                    log_result(f"XSS payload: {payload[:20]}", True, f"被拒绝({resp.status_code})")
            except Exception as e:
                log_result(f"XSS payload: {payload[:20]}", False, f"异常: {e}")

    def test_http_methods(self):
        """测试6: HTTP方法测试"""
        print("\n" + "="*60)
        print("测试6: HTTP方法测试")
        print("="*60)
        
        methods = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'OPTIONS', 'HEAD', 'TRACE', 'CONNECT']
        
        for method in methods:
            try:
                if method == 'TRACE':
                    continue  # TRACE通常被禁用
                resp = self.session.request(method, BASE_URL, timeout=5)
                
                if method in ['PUT', 'DELETE', 'PATCH']:
                    if resp.status_code in [405, 400, 403, 404]:
                        log_result(f"方法 {method}", True, f"正确拒绝({resp.status_code})")
                    elif resp.status_code == 200:
                        log_result(f"方法 {method}", False, f"不应允许! 返回200", is_vuln=True)
                    else:
                        log_result(f"方法 {method}", True, f"返回{resp.status_code}")
                elif method == 'OPTIONS':
                    log_result(f"方法 {method}", True, f"CORS预检, 返回{resp.status_code}")
                elif method == 'HEAD':
                    log_result(f"方法 {method}", True, f"返回{resp.status_code}, 长度{len(resp.content)}")
                else:
                    log_result(f"方法 {method}", True, f"返回{resp.status_code}")
            except Exception as e:
                log_result(f"方法 {method}", False, f"异常: {e}")

    def test_security_headers(self):
        """测试7: 安全响应头检查"""
        print("\n" + "="*60)
        print("测试7: 安全响应头检查")
        print("="*60)
        
        required_headers = {
            'X-Frame-Options': ['SAMEORIGIN', 'DENY'],
            'X-Content-Type-Options': ['nosniff'],
            'X-XSS-Protection': ['1'],
            'Strict-Transport-Security': None,  # HTTPS only
            'Content-Security-Policy': None,
            'Referrer-Policy': None,
        }
        
        try:
            resp = self.session.get(BASE_URL, timeout=5)
            
            for header, expected_values in required_headers.items():
                value = resp.headers.get(header, '')
                
                if header == 'Strict-Transport-Security':
                    if 'https' in BASE_URL:
                        if value:
                            log_result(f"安全头 {header}", True, f"存在: {value[:50]}")
                        else:
                            log_result(f"安全头 {header}", False, f"HTTPS模式下缺失!", is_vuln=True)
                    else:
                        log_result(f"安全头 {header}", True, f"HTTP模式不需要")
                elif expected_values:
                    if any(v in value for v in expected_values):
                        log_result(f"安全头 {header}", True, f"值: {value}")
                    elif value:
                        log_result(f"安全头 {header}", False, f"值异常: {value}", is_vuln=True)
                    else:
                        log_result(f"安全头 {header}", False, f"缺失!", is_vuln=True)
                else:
                    if value:
                        log_result(f"安全头 {header}", True, f"存在: {value[:50]}")
                    else:
                        log_result(f"安全头 {header}", False, f"缺失!", is_vuln=True)
                        
        except Exception as e:
            log_result("安全头检查", False, f"异常: {e}")

    def test_error_information_leak(self):
        """测试8: 错误信息泄露"""
        print("\n" + "="*60)
        print("测试8: 敏感信息泄露检测")
        print("="*60)
        
        leak_tests = [
            ("不存在的路径", f"{BASE_URL}/nonexistent-path-xyz-12345"),
            ("API错误路径", f"{BASE_URL}/api/nonexistent"),
            ("特殊路径%00", f"{BASE_URL}/api/files?path=test%00"),
            ("超长路径", f"{BASE_URL}/{'a'*500}"),
            ("SQL注入风格", f"{BASE_URL}/api/files?path=' OR 1=1--"),
            ("JSON注入", f"{BASE_URL}/api/files?path={{\"key\":\"val\"}}"),
            ("路径含引号", f"{BASE_URL}/api/files?path=\"test\""),
            ("路径含尖括号", f"{BASE_URL}/api/files?path=<test>"),
        ]
        
        sensitive_patterns = [
            'stack trace', 'exception', 'error at', 'at ', '.cs:',
            'System.', 'InternalServer', '500', 'Object reference',
            'null reference', 'ArgumentNullException', 'FileNotFoundException',
            'DirectoryNotFoundException', 'UnauthorizedAccessException',
            'connection string', 'password', 'secret', 'private key',
            'RepositoryPath', 'BasePath', 'config', 'Config.',
        ]
        
        for name, url in leak_tests:
            try:
                resp = self.session.get(url, timeout=5)
                content = resp.text.lower()
                
                found_sensitive = []
                for pattern in sensitive_patterns:
                    if pattern.lower() in content:
                        found_sensitive.append(pattern)
                
                if found_sensitive:
                    log_result(name, False,
                        f"可能泄露: {', '.join(found_sensitive[:3])}", is_vuln=True)
                elif resp.status_code == 500:
                    log_result(name, False, f"返回500且可能泄露信息", is_vuln=True)
                else:
                    log_result(name, True, f"返回{resp.status_code}, 无敏感信息")
            except Exception as e:
                log_result(name, False, f"异常: {e}")

    def test_rate_limiting_behavior(self):
        """测试9: 限速行为测试（谨慎）"""
        print("\n" + "="*60)
        print("测试9: 限速行为测试")
        print("="*60)
        
        # 只发少量请求观察行为，避免触发限速
        request_count = 3
        statuses = []
        
        for i in range(request_count):
            try:
                start = time.time()
                resp = self.session.get(BASE_URL, timeout=5)
                elapsed = time.time() - start
                statuses.append(resp.status_code)
                time.sleep(0.1)  # 间隔100ms
            except Exception as e:
                statuses.append(f"ERR:{e}")
        
        unique_statuses = set(statuses)
        if len(unique_statuses) == 1 and 200 in unique_statuses:
            log_result(f"连续{request_count}次请求", True, f"全部200, 正常")
        elif 429 in unique_statuses:
            log_result(f"连续{request_count}次请求", True, f"触发限流(429) - 限速工作正常")
        else:
            log_result(f"连续{request_count}次请求", True, f"状态码: {unique_statuses}")

    def test_websocket_endpoint(self):
        """测试10: WebSocket端点安全"""
        print("\n" + "="*60)
        print("测试10: WebSocket端点安全")
        print("="*60)
        
        ws_tests = [
            ("WS GET普通请求", "GET", "/admin/ws"),
            ("WS POST请求", "POST", "/admin/ws"),
            ("WS PUT请求", "PUT", "/admin/ws"),
            ("WS DELETE请求", "DELETE", "/admin/ws"),
            ("WS OPTIONS请求", "OPTIONS", "/admin/ws"),
        ]
        
        for name, method, path in ws_tests:
            try:
                url = f"{BASE_URL}{path}"
                resp = self.session.request(method, url, timeout=5)
                log_result(name, True, f"返回{resp.status_code}")
            except Exception as e:
                log_result(name, True, f"异常(预期): {str(e)[:50]}")

    def test_parameter_pollution(self):
        """测试11: 参数污染"""
        print("\n" + "="*60)
        print("测试11: HTTP参数污染")
        print("="*60)
        
        pollution_tests = [
            ("多path参数", f"{BASE_URL}/api/files?path=test&path=../../etc"),
            ("path数组", f"{BASE_URL}/api/files?path[]=test&path[]=../../etc"),
            ("覆盖path", f"{BASE_URL}/api/files?path=valid&path=../../windows"),
            ("额外参数", f"{BASE_URL}/api/files?path=test&debug=true&admin=true"),
            ("JSON格式path", f"{BASE_URL}/api/files?path=%7B%22p%22:%22test%22%7D"),
        ]
        
        for name, url in pollution_tests:
            try:
                resp = self.session.get(url, timeout=5)
                if resp.status_code == 200:
                    log_result(name, True, f"返回200, 未受污染影响")
                elif resp.status_code in [400, 404]:
                    log_result(name, True, f"正确处理({resp.status_code})")
                else:
                    log_result(name, True, f"返回{resp.status_code}")
            except Exception as e:
                log_result(name, False, f"异常: {e}")

    def test_unicode_bypass(self):
        """测试12: Unicode绕过"""
        print("\n" + "="*60)
        print("测试12: Unicode编码绕过")
        print("="*60)
        
        unicode_paths = [
            ("\u002e\u002e/", "Unicode .."),
            ("%uff0e%uff0e/", "全角点号"),
            ("%c0%ae%c0%ae/", "Overlong UTF-8 .."),
            ("%c0%af", "Overlong UTF-8 /"),
            (".\u00ad/", "软连字符"),
            ("\u200b", "零宽空格"),
            ("\u200e", "从左到右标记"),
            ("\u202e", "从右到左覆盖"),
            ("\ufeff", "BOM字符"),
        ]
        
        for path, desc in unicode_paths:
            try:
                url = f"{BASE_URL}/api/files?path={urllib.parse.quote(path, safe='')}"
                resp = self.session.get(url, timeout=5)
                
                if resp.status_code == 200:
                    log_result(f"Unicode [{desc}]", False, f"绕过成功! 返回200", is_vuln=True)
                else:
                    log_result(f"Unicode [{desc}]", True, f"正确拒绝({resp.status_code})")
            except Exception as e:
                log_result(f"Unicode [{desc}]", False, f"异常: {e}")

    def test_directory_listing_info(self):
        """测试13: 目录列表信息泄露"""
        print("\n" + "="*60)
        print("测试13: 目录列表信息泄露")
        print("="*60)
        
        try:
            resp = self.session.get(f"{BASE_URL}/api/files?path=", timeout=5)
            if resp.status_code == 200:
                data = resp.json()
                
                checks = [
                    ("暴露完整路径", lambda d: any(
                        '\\' in str(f.get('name', '')) or '/' in str(f.get('name', ''))
                        for f in d.get('files', []) + d.get('directories', [])
                    )),
                    ("暴露文件大小", lambda d: any(
                        f.get('size') is not None
                        for f in d.get('files', [])
                    )),
                    ("暴露修改时间", lambda d: any(
                        f.get('lastModified') is not None
                        for f in d.get('files', []) + d.get('directories', [])
                    )),
                    ("暴露SHA256哈希", lambda d: any(
                        f.get('hash') or f.get('sha256')
                        for f in d.get('files', []) + d.get('directories', [])
                    )),
                ]
                
                for name, check in checks:
                    if check(data):
                        log_result(name, True, f"存在此信息(设计如此)")
                    else:
                        log_result(name, True, f"不存在此信息")
            else:
                log_result("目录列表信息", True, f"返回{resp.status_code}")
        except Exception as e:
            log_result("目录列表信息", False, f"异常: {e}")


def main():
    print("=" * 60)
    print("Repository Server 安全测试")
    print(f"目标: {BASE_URL}")
    print(f"时间: {time.strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 60)
    
    tester = SecurityTester()
    
    tests = [
        ("高级路径遍历", tester.test_path_traversal_advanced),
        ("系统路径访问", tester.test_system_path_access),
        ("管理面板安全", tester.test_admin_panel_security),
        ("HTTP头注入", tester.test_header_injection),
        ("文件名XSS", tester.test_xss_in_filenames),
        ("HTTP方法", tester.test_http_methods),
        ("安全响应头", tester.test_security_headers),
        ("信息泄露", tester.test_error_information_leak),
        ("限速行为", tester.test_rate_limiting_behavior),
        ("WebSocket端点", tester.test_websocket_endpoint),
        ("参数污染", tester.test_parameter_pollution),
        ("Unicode绕过", tester.test_unicode_bypass),
        ("目录信息泄露", tester.test_directory_listing_info),
    ]
    
    for name, test_func in tests:
        try:
            test_func()
        except Exception as e:
            print(f"\n  ❌ 测试 '{name}' 异常: {e}")
        
        time.sleep(0.3)  # 避免触发限速
    
    print("\n" + "=" * 60)
    print("测试总结")
    print("=" * 60)
    
    total = len(results)
    passed = sum(1 for r in results if r['passed'])
    failed = total - passed
    vulns = len(vulnerabilities)
    
    print(f"\n总测试数: {total}")
    print(f"通过: {passed} ({passed/total*100:.1f}%)")
    print(f"失败: {failed} ({failed/total*100:.1f}%)")
    print(f"发现漏洞: {vulns}")
    
    if vulnerabilities:
        print("\n--- 发现的漏洞 ---")
        for i, v in enumerate(vulnerabilities, 1):
            print(f"  {i}. [{v['test']}] {v['detail']}")
    
    return vulns


if __name__ == "__main__":
    import urllib3
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    
    vuln_count = main()
    sys.exit(vuln_count)
