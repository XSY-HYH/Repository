#!/usr/bin/env python3
"""
文件上传示例脚本（增强版）
用于向仓库服务器上传文件和文件夹
支持：
- 单个文件上传
- 整个文件夹递归上传
- 上传进度显示
- 配置文件保存常用设置
- 批量文件上传
- 错误重试机制
- HTTPS 支持（可选跳过证书验证）
"""

import requests
import os
import sys
import json
import time
import ssl
import urllib3
import socket
from tqdm import tqdm
from concurrent.futures import ThreadPoolExecutor, as_completed
from urllib.parse import quote
from requests.adapters import HTTPAdapter
from urllib3.util.ssl_ import create_urllib3_context

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

class TLSAdapter(HTTPAdapter):
    """自定义TLS适配器，支持更多协议版本"""
    def init_poolmanager(self, *args, **kwargs):
        ctx = create_urllib3_context()
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
        ctx.minimum_version = ssl.TLSVersion.TLSv1_2
        ctx.maximum_version = ssl.TLSVersion.TLSv1_3
        ctx.options |= ssl.OP_NO_SSLv2
        ctx.options |= ssl.OP_NO_SSLv3
        ctx.options |= ssl.OP_NO_TLSv1
        ctx.options |= ssl.OP_NO_TLSv1_1
        kwargs['ssl_context'] = ctx
        return super().init_poolmanager(*args, **kwargs)

CONFIG_FILE = os.path.join(os.path.expanduser("~"), ".repo_upload_config.json")
DEFAULT_CONFIG = {
    "server_url": "http://localhost:8000",
    "max_workers": 3,
    "chunk_size": 8192,
    "max_retries": 3,
    "timeout": 300,
    "verify_ssl": True,
    "debug": False
}

def load_config():
    """加载配置文件"""
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, 'r', encoding='utf-8') as f:
                config = json.load(f)
                return {**DEFAULT_CONFIG, **config}
        except Exception as e:
            print(f"[警告] 加载配置文件失败: {e}")
    return DEFAULT_CONFIG.copy()

def save_config(config):
    """保存配置到文件"""
    try:
        with open(CONFIG_FILE, 'w', encoding='utf-8') as f:
            json.dump(config, f, indent=2, ensure_ascii=False)
        print(f"配置已保存到 {CONFIG_FILE}")
    except Exception as e:
        print(f"保存配置文件失败: {e}")

def debug_log(config, message):
    """调试日志输出"""
    if config.get("debug", False):
        print(f"[调试] {message}")

def upload_file_with_progress(local_file_path, remote_path, server_url, config):
    """
    上传文件到仓库服务器（带进度条）
    
    Args:
        local_file_path: 本地文件路径
        remote_path: 远程相对路径（如：/documents/test.txt 或 documents/test.txt）
        server_url: 服务器地址
        config: 配置字典
    """
    
    if not os.path.exists(local_file_path):
        print(f"[错误] 本地文件不存在 - {local_file_path}")
        return False
    
    file_size = os.path.getsize(local_file_path)
    file_name = os.path.basename(local_file_path)
    
    remote_path = remote_path.strip('/')
    if '/' in remote_path:
        folder_path = os.path.dirname(remote_path)
    else:
        folder_path = ''
    
    if folder_path:
        encoded_folder = quote(folder_path, safe='')
        upload_url = f"{server_url.rstrip('/')}/api/upload/{encoded_folder}"
    else:
        upload_url = f"{server_url.rstrip('/')}/api/upload"
    
    verify_ssl = config.get("verify_ssl", True)
    
    debug_log(config, f"上传URL: {upload_url}")
    debug_log(config, f"SSL证书验证: {verify_ssl}")
    debug_log(config, f"本地文件: {local_file_path}")
    debug_log(config, f"文件名: {file_name}")
    debug_log(config, f"文件大小: {file_size} bytes")
    
    for attempt in range(config["max_retries"]):
        try:
            print(f"[上传] 正在上传文件...")
            print(f"   本地文件: {local_file_path}")
            print(f"   远程路径: {remote_path}")
            print(f"   文件大小: {format_size(file_size)}")
            print(f"   尝试 {attempt + 1}/{config['max_retries']}")
            
            debug_log(config, f"发送POST请求到: {upload_url}")
            
            session = requests.Session()
            if is_https := server_url.lower().startswith('https://'):
                if not verify_ssl:
                    session.mount('https://', TLSAdapter())
            
            with open(local_file_path, 'rb') as f:
                files = {
                    'file': (file_name, f, 'application/octet-stream')
                }
                
                headers = {
                    'Accept': 'application/json',
                }
                
                response = session.post(
                    upload_url, 
                    files=files,
                    headers=headers,
                    timeout=config["timeout"],
                    verify=verify_ssl
                )
            
            debug_log(config, f"响应状态码: {response.status_code}")
            debug_log(config, f"响应头: {dict(response.headers)}")
            
            if response.status_code == 200:
                result = response.json()
                print(f"[成功] 文件上传成功！")
                print(f"   文件名: {result.get('fileName')}")
                print(f"   文件路径: {result.get('filePath')}")
                print(f"   文件大小: {result.get('fileSize')}")
                return True
            elif response.status_code == 403:
                error_msg = response.text
                try:
                    error_data = response.json()
                    error_msg = error_data.get('message', error_data.get('error', response.text))
                except:
                    pass
                print(f"[失败] {error_msg}")
                return False
            elif response.status_code == 409:
                print("[失败] 文件已存在且不允许覆盖")
                return False
            elif response.status_code == 413:
                print(f"[失败] 文件过大")
                return False
            elif response.status_code == 400:
                error_msg = response.text
                try:
                    error_data = response.json()
                    error_msg = error_data.get('message', error_data.get('error', response.text))
                except:
                    pass
                print(f"[失败] {error_msg}")
                return False
            else:
                print(f"[失败] 服务器错误 (状态码: {response.status_code}): {response.text}")
                if attempt < config["max_retries"] - 1:
                    wait_time = 2 ** attempt
                    print(f"[等待] 将在 {wait_time} 秒后重试...")
                    time.sleep(wait_time)
                continue
                
        except requests.exceptions.SSLError as e:
            print(f"[失败] SSL证书验证失败: {e}")
            debug_log(config, f"SSL错误详情: {type(e).__name__}: {str(e)}")
            print("[提示] 如果服务器使用自签名证书，可以在配置中设置 verify_ssl: false")
            print("[提示] 或者在输入服务器地址后选择跳过证书验证")
            return False
        except requests.exceptions.ConnectTimeout as e:
            print(f"[失败] 连接超时: {e}")
            debug_log(config, f"超时详情: {str(e)}")
            if attempt < config["max_retries"] - 1:
                wait_time = 2 ** attempt
                print(f"[等待] 将在 {wait_time} 秒后重试...")
                time.sleep(wait_time)
            continue
        except requests.exceptions.ReadTimeout as e:
            print(f"[失败] 读取超时: {e}")
            debug_log(config, f"读取超时详情: {str(e)}")
            if attempt < config["max_retries"] - 1:
                wait_time = 2 ** attempt
                print(f"[等待] 将在 {wait_time} 秒后重试...")
                time.sleep(wait_time)
            continue
        except requests.exceptions.ConnectionError as e:
            print(f"[失败] 无法连接到服务器: {e}")
            debug_log(config, f"连接错误详情: {type(e).__name__}: {str(e)}")
            error_str = str(e).lower()
            if "name or service not known" in error_str or "getaddrinfo failed" in error_str:
                print("[提示] 无法解析服务器地址，请检查域名是否正确")
            elif "connection refused" in error_str:
                print("[提示] 连接被拒绝，请检查服务器是否正在运行以及端口是否正确")
            elif "network is unreachable" in error_str:
                print("[提示] 网络不可达，请检查网络连接")
            elif "timed out" in error_str:
                print("[提示] 连接超时，请检查服务器地址和端口是否正确")
            if attempt < config["max_retries"] - 1:
                wait_time = 2 ** attempt
                print(f"[等待] 将在 {wait_time} 秒后重试...")
                time.sleep(wait_time)
            continue
        except requests.exceptions.RequestException as e:
            print(f"[失败] 网络错误: {e}")
            debug_log(config, f"请求异常详情: {type(e).__name__}: {str(e)}")
            if attempt < config["max_retries"] - 1:
                wait_time = 2 ** attempt
                print(f"[等待] 将在 {wait_time} 秒后重试...")
                time.sleep(wait_time)
            continue
        except Exception as e:
            print(f"[失败] 上传过程中发生错误: {e}")
            debug_log(config, f"未知异常详情: {type(e).__name__}: {str(e)}")
            import traceback
            traceback.print_exc()
            return False
    
    print(f"[失败] 已达到最大重试次数 ({config['max_retries']})")
    return False

def upload_folder(local_folder_path, remote_folder_path, server_url, config):
    """
    递归上传整个文件夹
    
    Args:
        local_folder_path: 本地文件夹路径
        remote_folder_path: 远程文件夹路径
        server_url: 服务器地址
        config: 配置字典
    """
    
    if not os.path.exists(local_folder_path):
        print(f"[错误] 本地文件夹不存在 - {local_folder_path}")
        return False
    
    if not os.path.isdir(local_folder_path):
        print(f"[错误] 指定路径不是文件夹 - {local_folder_path}")
        return False
    
    remote_folder_path = remote_folder_path.strip('/')
    if remote_folder_path and not remote_folder_path.endswith('/'):
        remote_folder_path += '/'
    
    all_files = []
    for root, dirs, files in os.walk(local_folder_path):
        for file in files:
            local_path = os.path.join(root, file)
            rel_path = os.path.relpath(local_path, local_folder_path)
            remote_path = remote_folder_path + rel_path.replace(os.sep, '/')
            all_files.append((local_path, remote_path))
    
    if not all_files:
        print("[警告] 文件夹为空，无需上传")
        return True
    
    print(f"[信息] 发现 {len(all_files)} 个文件待上传")
    
    success_count = 0
    fail_count = 0
    
    with ThreadPoolExecutor(max_workers=min(config["max_workers"], len(all_files))) as executor:
        future_to_file = {executor.submit(upload_file_with_progress, local_path, remote_path, server_url, config): (local_path, remote_path) 
                         for local_path, remote_path in all_files}
        
        with tqdm(total=len(all_files), desc="[进度] 总体进度") as pbar:
            for future in as_completed(future_to_file):
                local_path, remote_path = future_to_file[future]
                try:
                    success = future.result()
                    if success:
                        success_count += 1
                    else:
                        fail_count += 1
                except Exception as e:
                    print(f"[错误] 处理文件时发生异常 {local_path}: {e}")
                    fail_count += 1
                finally:
                    pbar.update(1)
    
    print("\n" + "=" * 50)
    print(f"[统计] 上传统计")
    print(f"[成功] 成功: {success_count}")
    print(f"[失败] 失败: {fail_count}")
    print(f"[统计] 成功率: {success_count/len(all_files)*100:.1f}%" if all_files else "0%")
    
    return fail_count == 0

def format_size(size_bytes):
    """格式化文件大小显示"""
    for unit in ['B', 'KB', 'MB', 'GB']:
        if size_bytes < 1024.0:
            return f"{size_bytes:.2f} {unit}"
        size_bytes /= 1024.0
    return f"{size_bytes:.2f} TB"

def get_user_input(config):
    """获取用户输入"""
    
    print("=" * 50)
    print("[工具] 仓库服务器文件上传工具（增强版）")
    print("=" * 50)
    print("支持单个文件上传和整个文件夹递归上传")
    
    while True:
        upload_type = input("\n[选择] 请选择上传类型 (1: 文件, 2: 文件夹): ").strip()
        if upload_type in ['1', '2']:
            upload_type = int(upload_type)
            break
        print("[警告] 请输入 1 或 2")
    
    while True:
        prompt = "[输入] 请输入要上传的本地文件路径: " if upload_type == 1 else "[输入] 请输入要上传的本地文件夹路径: "
        local_path = input(prompt).strip()
        
        if not local_path:
            print("[警告] 路径不能为空，请重新输入")
            continue
            
        local_path = os.path.expanduser(local_path)
        
        if os.path.exists(local_path):
            if (upload_type == 1 and os.path.isfile(local_path)) or \
               (upload_type == 2 and os.path.isdir(local_path)):
                break
            else:
                print("[警告] 输入的路径类型不正确，请重新输入")
        else:
            print("[警告] 文件/文件夹不存在，请重新输入")
    
    while True:
        if upload_type == 1:
            default_remote_name = os.path.basename(local_path)
            prompt = f"[输入] 请输入远程相对路径（如：documents/{default_remote_name}）: "
        else:
            default_remote_name = os.path.basename(local_path)
            prompt = f"[输入] 请输入远程文件夹路径（如：documents/{default_remote_name}/）: "
            
        remote_path = input(prompt).strip()
        
        if not remote_path:
            remote_path = default_remote_name if upload_type == 1 else f"{default_remote_name}/"
        
        remote_path = remote_path.lstrip('/')
        
        if upload_type == 2 and not remote_path.endswith('/'):
            remote_path += '/'
            
        break
    
    server_url = input(f"\n[输入] 请输入服务器地址（默认: {config['server_url']}）: ").strip()
    if not server_url:
        server_url = config["server_url"]
    
    server_url = server_url.rstrip('/')
    
    is_https = server_url.lower().startswith('https://')
    verify_ssl = config.get("verify_ssl", True)
    
    if is_https:
        ssl_choice = input(f"\n[SSL] 是否验证SSL证书？(Y/n，默认验证): ").strip().lower()
        if ssl_choice in ['n', 'no', '否', '跳过']:
            verify_ssl = False
            print("[警告] 已禁用SSL证书验证，连接可能不安全！")
    
    debug_choice = input(f"\n[调试] 是否启用调试模式？(y/N): ").strip().lower()
    debug_enabled = debug_choice in ['y', 'yes', '是', '确认']
    if debug_enabled:
        print("[调试] 调试模式已启用")
    
    current_config = {**config, "verify_ssl": verify_ssl, "debug": debug_enabled}
    
    save_conf = input("\n[保存] 是否保存当前设置为默认配置？(y/n): ").strip().lower()
    if save_conf in ['y', 'yes', '是', '确认']:
        config["server_url"] = server_url
        config["verify_ssl"] = verify_ssl
        config["debug"] = debug_enabled
        save_config(config)
    
    return upload_type, local_path, remote_path, server_url, current_config

def batch_upload_from_file(file_list_path, server_url, config):
    """
    从文件列表批量上传
    文件列表格式：每行一个本地路径和远程路径，用逗号分隔
    """
    if not os.path.exists(file_list_path):
        print(f"[错误] 文件列表不存在 - {file_list_path}")
        return False
    
    try:
        file_pairs = []
        with open(file_list_path, 'r', encoding='utf-8') as f:
            for line_num, line in enumerate(f, 1):
                line = line.strip()
                if not line or line.startswith('#'):
                    continue
                
                try:
                    local, remote = line.split(',', 1)
                    local = local.strip()
                    remote = remote.strip()
                    
                    if os.path.exists(local):
                        file_pairs.append((local, remote))
                    else:
                        print(f"[警告] 第{line_num}行：本地文件不存在 - {local}")
                except ValueError:
                    print(f"[警告] 第{line_num}行：格式错误，应为'本地路径,远程路径'")
        
        if not file_pairs:
            print("[警告] 没有有效的文件对可供上传")
            return True
        
        print(f"[信息] 开始批量上传 {len(file_pairs)} 个文件")
        
        success_count = 0
        for local_path, remote_path in file_pairs:
            print(f"\n--- 正在上传 {local_path} ---")
            if upload_file_with_progress(local_path, remote_path, server_url, config):
                success_count += 1
        
        print("\n" + "=" * 50)
        print(f"[统计] 批量上传统计")
        print(f"[成功] 成功: {success_count}")
        print(f"[失败] 失败: {len(file_pairs) - success_count}")
        print(f"[统计] 成功率: {success_count/len(file_pairs)*100:.1f}%")
        
        return success_count == len(file_pairs)
        
    except Exception as e:
        print(f"[错误] 批量上传过程中发生错误: {e}")
        return False

def main():
    """主函数"""
    
    config = load_config()
    
    try:
        if len(sys.argv) > 1:
            if sys.argv[1] == '--batch' and len(sys.argv) == 3:
                print("[批量] 批量上传模式")
                success = batch_upload_from_file(sys.argv[2], config["server_url"], config)
                return 0 if success else 1
        
        while True:
            upload_type, local_path, remote_path, server_url, current_config = get_user_input(config)
            
            print("\n" + "=" * 50)
            print("[确认] 上传信息确认")
            print("=" * 50)
            print(f"上传类型: {'文件' if upload_type == 1 else '文件夹'}")
            print(f"本地路径: {local_path}")
            print(f"远程路径: {remote_path}")
            print(f"服务器: {server_url}")
            print(f"SSL验证: {'启用' if current_config.get('verify_ssl', True) else '禁用'}")
            print(f"调试模式: {'启用' if current_config.get('debug', False) else '禁用'}")
            
            if upload_type == 2:
                total_files = sum(len(files) for _, _, files in os.walk(local_path))
                print(f"预计上传文件数: {total_files}")
            
            confirm = input("\n是否开始上传？(y/n): ").strip().lower()
            
            if confirm in ['y', 'yes', '是', '确认']:
                print("\n[开始] 开始上传...")
                
                if upload_type == 1:
                    success = upload_file_with_progress(local_path, remote_path, server_url, current_config)
                else:
                    success = upload_folder(local_path, remote_path, server_url, current_config)
                
                if success:
                    print("\n[完成] 上传完成！")
                else:
                    print("\n[失败] 上传失败！")
            else:
                print("\n[取消] 上传已取消")
            
            continue_upload = input("\n[继续] 是否继续上传其他文件？(y/n): ").strip().lower()
            if continue_upload not in ['y', 'yes', '是', '确认']:
                print("\n[再见] 感谢使用，再见！")
                break
                
    except KeyboardInterrupt:
        print("\n\n[中断] 用户中断操作")
        return 130
    except Exception as e:
        print(f"\n[错误] 程序执行错误: {e}")
        import traceback
        traceback.print_exc()
        return 1

if __name__ == "__main__":
    sys.exit(main())
