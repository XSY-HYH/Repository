let ws = null;
let sessionId = null;
let k = null;
let currentKey = null;
let currentPath = '';
let moveTargetPath = null;
let moveTargetType = null;

async function sha256(message) {
    const msgBuffer = new TextEncoder().encode(message);
    const hashBuffer = await crypto.subtle.digest('SHA-256', msgBuffer);
    return new Uint8Array(hashBuffer);
}

function base64ToBytes(base64) {
    const binaryString = atob(base64);
    const bytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
        bytes[i] = binaryString.charCodeAt(i);
    }
    return bytes;
}

async function importKey(keyBytes) {
    return await crypto.subtle.importKey(
        'raw',
        keyBytes,
        { name: 'AES-CBC' },
        false,
        ['encrypt', 'decrypt']
    );
}

async function encrypt(keyBytes, plaintext) {
    const iv = crypto.getRandomValues(new Uint8Array(16));
    const cryptoKey = await importKey(keyBytes);
    const encoded = new TextEncoder().encode(plaintext);
    const ciphertext = await crypto.subtle.encrypt(
        { name: 'AES-CBC', iv: iv },
        cryptoKey,
        encoded
    );
    const result = new Uint8Array(iv.length + ciphertext.byteLength);
    result.set(iv, 0);
    result.set(new Uint8Array(ciphertext), iv.length);
    return result;
}

async function decrypt(keyBytes, ciphertext) {
    const iv = ciphertext.slice(0, 16);
    const data = ciphertext.slice(16);
    const cryptoKey = await importKey(keyBytes);
    const decrypted = await crypto.subtle.decrypt(
        { name: 'AES-CBC', iv: iv },
        cryptoKey,
        data
    );
    return new TextDecoder().decode(decrypted);
}

async function login() {
    const username = document.getElementById('username').value.trim();
    const password = document.getElementById('password').value;
    const errorDiv = document.getElementById('loginError');
    const loginBtn = document.getElementById('loginBtn');

    if (!username || !password) {
        errorDiv.textContent = '请输入用户名和密码';
        return;
    }

    errorDiv.textContent = '';
    loginBtn.disabled = true;
    loginBtn.textContent = '连接中...';

    try {
        k = await sha256(password);

        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = protocol + '//' + window.location.host + '/admin/ws';

        ws = new WebSocket(wsUrl);
        ws.binaryType = 'arraybuffer';

        ws.onopen = async () => {
            const encrypted = await encrypt(k, username);
            ws.send(encrypted);
        };

        ws.onmessage = async (event) => {
            try {
                const ciphertext = new Uint8Array(event.data);

                let plaintext;
                try {
                    plaintext = await decrypt(k, ciphertext);
                } catch (decryptError) {
                    console.error('解密失败:', decryptError);
                    errorDiv.textContent = '解密失败: ' + decryptError.message;
                    loginBtn.disabled = false;
                    loginBtn.textContent = '登录';
                    return;
                }

                let response;
                try {
                    response = JSON.parse(plaintext);
                } catch (parseError) {
                    console.error('JSON解析失败:', parseError);
                    errorDiv.textContent = '响应格式错误';
                    loginBtn.disabled = false;
                    loginBtn.textContent = '登录';
                    return;
                }

                if (response.Success) {
                    sessionId = response.NewId;
                    currentKey = base64ToBytes(response.NewId);
                    ws.onmessage = null;
                    document.getElementById('loginContainer').style.display = 'none';
                    document.getElementById('adminContainer').style.display = 'block';
                    loadDirectory('');
                } else {
                    errorDiv.textContent = response.Message || '登录失败';
                    loginBtn.disabled = false;
                    loginBtn.textContent = '登录';
                }
            } catch (e) {
                console.error('处理消息异常:', e);
                errorDiv.textContent = '服务器响应错误: ' + e.message;
                loginBtn.disabled = false;
                loginBtn.textContent = '登录';
            }
        };

        ws.onerror = () => {
            errorDiv.textContent = '连接失败';
            loginBtn.disabled = false;
            loginBtn.textContent = '登录';
        };

        ws.onclose = () => {
            if (!sessionId) {
                errorDiv.textContent = '连接已关闭';
                loginBtn.disabled = false;
                loginBtn.textContent = '登录';
            }
        };

    } catch (e) {
        errorDiv.textContent = '登录失败: ' + e.message;
        loginBtn.disabled = false;
        loginBtn.textContent = '登录';
    }
}

async function sendOperation(action, path, newPath, settings) {
    return new Promise((resolve, reject) => {
        if (!ws || ws.readyState !== WebSocket.OPEN) {
            reject(new Error('未连接'));
            return;
        }

        const operation = {
            SessionId: sessionId,
            Action: action,
            Path: path,
            NewPath: newPath,
            Settings: settings
        };

        const handler = async (event) => {
            try {
                const ciphertext = new Uint8Array(event.data);
                const plaintext = await decrypt(currentKey, ciphertext);
                const response = JSON.parse(plaintext);

                if (response.Type === 'server_notification') {
                    alert('服务器通知: ' + response.Message);
                    ws.close();
                    document.getElementById('adminContainer').style.display = 'none';
                    document.getElementById('loginContainer').style.display = 'block';
                    errorDiv.textContent = '服务器已关闭';
                    ws.removeEventListener('message', handler);
                    reject(new Error('服务器已关闭'));
                    return;
                }

                ws.removeEventListener('message', handler);

                if (response.NewId) {
                    sessionId = response.NewId;
                    currentKey = base64ToBytes(response.NewId);
                }

                resolve(response);
            } catch (e) {
                ws.removeEventListener('message', handler);
                reject(e);
            }
        };

        ws.addEventListener('message', handler);

        encrypt(currentKey, JSON.stringify(operation)).then(encrypted => {
            ws.send(encrypted);
        }).catch(reject);
    });
}

async function loadDirectory(path) {
    currentPath = path;
    updateBreadcrumb();

    try {
        const response = await sendOperation('list_directory', path, null);

        if (response.Success) {
            renderFileList(response.Data);
        } else {
            alert('加载目录失败: ' + response.Message);
        }
    } catch (e) {
        alert('加载目录失败: ' + e.message);
    }
}

function updateBreadcrumb() {
    const breadcrumb = document.getElementById('breadcrumb');
    let html = '<a href="#" onclick="navigateTo(\'\')">根目录</a>';

    if (currentPath) {
        const parts = currentPath.split('/').filter(p => p);
        let path = '';

        parts.forEach((part, index) => {
            path += (path ? '/' : '') + part;
            html += ' / <a href="#" onclick="navigateTo(\'' + path + '\')">' + escapeHtml(part) + '</a>';
        });
    }

    breadcrumb.innerHTML = html;
}

function renderFileList(data) {
    const tbody = document.getElementById('fileList');
    let html = '';

    if (currentPath) {
        const parentPath = currentPath.substring(0, currentPath.lastIndexOf('/'));
        html += '<tr><td><a href="#" onclick="navigateTo(\'' + escapeHtml(parentPath) + '\')" class="dir">../</a></td><td>-</td><td>-</td></tr>';
    }

    if (data.directories && data.directories.length > 0) {
        data.directories.forEach(dir => {
            html += '<tr><td><a href="#" onclick="navigateTo(\'' + escapeHtml(dir.path) + '\')" class="dir">' + escapeHtml(dir.name) + '/</a></td><td>-</td><td class="actions"><button class="action-btn btn-delete" onclick="deleteDirectory(\'' + escapeHtml(dir.path) + '\')">回收站</button><button class="action-btn btn-move" onclick="showMoveModal(\'' + escapeHtml(dir.path) + '\', \'directory\')">移动</button></td></tr>';
        });
    }

    if (data.files && data.files.length > 0) {
        data.files.forEach(file => {
            html += '<tr><td class="file">' + escapeHtml(file.name) + '</td><td>' + formatFileSize(file.size) + '</td><td class="actions"><button class="action-btn" style="background: #17a2b8; color: white;" onclick="downloadFile(\'' + escapeHtml(file.path) + '\')">下载</button><button class="action-btn btn-delete" onclick="deleteFile(\'' + escapeHtml(file.path) + '\')">回收站</button><button class="action-btn btn-move" onclick="showMoveModal(\'' + escapeHtml(file.path) + '\', \'file\')">移动</button></td></tr>';
        });
    }

    if (!html) {
        html = '<tr><td colspan="3" style="text-align: center; color: #6c757d;">空目录</td></tr>';
    }

    tbody.innerHTML = html;
}

function navigateTo(path) {
    loadDirectory(path);
    return false;
}

function downloadFile(path) {
    const downloadUrl = '/api/download/' + encodeURIComponent(path);
    window.open(downloadUrl, '_blank');
}

async function deleteFile(path) {
    if (!confirm('确定要将此文件移至回收站吗？')) return;

    try {
        const response = await sendOperation('delete_file', path, null);
        if (response.Success) {
            loadDirectory(currentPath);
        } else {
            alert('操作失败: ' + response.Message);
        }
    } catch (e) {
        alert('操作失败: ' + e.message);
    }
}

async function deleteDirectory(path) {
    if (!confirm('确定要将此目录及其所有内容移至回收站吗？')) return;

    try {
        const response = await sendOperation('delete_directory', path, null);
        if (response.Success) {
            loadDirectory(currentPath);
        } else {
            alert('操作失败: ' + response.Message);
        }
    } catch (e) {
        alert('操作失败: ' + e.message);
    }
}

function showMoveModal(path, type) {
    moveTargetPath = path;
    moveTargetType = type;
    document.getElementById('newPathInput').value = path;
    document.getElementById('moveModal').style.display = 'block';
}

function closeMoveModal() {
    document.getElementById('moveModal').style.display = 'none';
    moveTargetPath = null;
    moveTargetType = null;
}

async function confirmMove() {
    const newPath = document.getElementById('newPathInput').value.trim();

    if (!newPath) {
        alert('请输入新路径');
        return;
    }

    try {
        const action = moveTargetType === 'file' ? 'move_file' : 'move_directory';
        const response = await sendOperation(action, moveTargetPath, newPath);

        if (response.Success) {
            closeMoveModal();
            loadDirectory(currentPath);
        } else {
            alert('移动失败: ' + response.Message);
        }
    } catch (e) {
        alert('移动失败: ' + e.message);
    }
}

function showCreateDirModal() {
    document.getElementById('dirNameInput').value = '';
    document.getElementById('createDirModal').style.display = 'block';
}

function closeCreateDirModal() {
    document.getElementById('createDirModal').style.display = 'none';
}

async function confirmCreateDir() {
    const dirName = document.getElementById('dirNameInput').value.trim();

    if (!dirName) {
        alert('请输入目录名称');
        return;
    }

    const path = currentPath ? currentPath + '/' + dirName : dirName;

    try {
        const response = await sendOperation('create_directory', path, null);

        if (response.Success) {
            closeCreateDirModal();
            loadDirectory(currentPath);
        } else {
            alert('创建失败: ' + response.Message);
        }
    } catch (e) {
        alert('创建失败: ' + e.message);
    }
}

function showUploadModal() {
    document.getElementById('fileInput').click();
}

function handleFileSelect(event) {
    const file = event.target.files[0];
    if (!file) return;

    uploadFile(file);
    event.target.value = '';
}

async function uploadFile(file) {
    const formData = new FormData();
    formData.append('file', file);

    try {
        const response = await fetch('/admin/api/upload?path=' + encodeURIComponent(currentPath), {
            method: 'POST',
            body: formData
        });

        const result = await response.json();

        if (response.ok) {
            loadDirectory(currentPath);
        } else {
            alert('上传失败: ' + (result.message || result));
        }
    } catch (e) {
        alert('上传失败: ' + e.message);
    }
}

function logout() {
    if (ws) {
        ws.close();
    }
    ws = null;
    sessionId = null;
    k = null;
    currentKey = null;
    currentPath = '';
    document.getElementById('adminContainer').style.display = 'none';
    document.getElementById('loginContainer').style.display = 'block';
    document.getElementById('username').value = '';
    document.getElementById('password').value = '';
    document.getElementById('loginBtn').disabled = false;
    document.getElementById('loginBtn').textContent = '登录';
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function formatFileSize(bytes) {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

document.getElementById('password').addEventListener('keypress', function(e) {
    if (e.key === 'Enter') {
        login();
    }
});

window.onclick = function(event) {
    if (event.target.classList.contains('modal')) {
        event.target.style.display = 'none';
    }
};

function showLogsModal() {
    document.getElementById('logsModal').style.display = 'block';
    document.getElementById('logsList').style.display = 'block';
    document.getElementById('logContent').style.display = 'none';
    loadLogFiles();
}

function closeLogsModal() {
    document.getElementById('logsModal').style.display = 'none';
}

function showSettingsModal() {
    document.getElementById('settingsModal').style.display = 'block';
    loadSettings();
}

function closeSettingsModal() {
    document.getElementById('settingsModal').style.display = 'none';
}

async function loadSettings() {
    const content = document.getElementById('settingsContent');
    content.innerHTML = '加载中...';

    try {
        const response = await sendOperation('get_settings', null, null);

        if (response.Success && response.Data) {
            const settings = response.Data.settings;
            const rateLimitStatus = response.Data.rateLimitStatus;

            let html = '<div style="display: grid; gap: 15px;">';
            
            html += '<fieldset style="border: 1px solid #dee2e6; padding: 15px; border-radius: 4px;">';
            html += '<legend style="font-weight: bold; padding: 0 10px;">请求限速</legend>';
            html += '<label style="display: flex; align-items: center; gap: 10px; margin-bottom: 10px;">';
            html += '<input type="checkbox" id="RequestThrottling" ' + (settings.RequestThrottling ? 'checked' : '') + '>';
            html += '<span>启用请求限速</span></label>';
            html += '<div style="margin-bottom: 10px;"><label>每秒最大请求值: </label>';
            html += '<input type="number" id="MaximumRequestsPerSecond" value="' + settings.MaximumRequestsPerSecond + '" style="width: 100px; padding: 5px;"></div>';
            html += '<div style="margin-bottom: 10px;"><label>每秒告警请求值: </label>';
            html += '<input type="number" id="AlarmRequestsPerSecond" value="' + settings.AlarmRequestsPerSecond + '" style="width: 100px; padding: 5px;"></div>';
            html += '<div style="margin-bottom: 10px;"><label>封禁时长(分钟): </label>';
            html += '<input type="number" id="BlockDurationMinutes" value="' + settings.BlockDurationMinutes + '" style="width: 100px; padding: 5px;"></div>';
            html += '<div style="margin-bottom: 10px;"><label>已封禁的IP: </label>';
            html += '<input type="text" id="BlockedIPs" value="' + settings.BlockedIPs + '" style="width: 200px; padding: 5px;"></div>';
            html += '<div><label>临时封禁的IP: </label>';
            html += '<input type="text" id="TemporarilyBlockedIPs" value="' + settings.TemporarilyBlockedIPs + '" style="width: 200px; padding: 5px;"></div>';
            html += '</fieldset>';

            html += '<fieldset style="border: 1px solid #dee2e6; padding: 15px; border-radius: 4px;">';
            html += '<legend style="font-weight: bold; padding: 0 10px;">文件上传</legend>';
            html += '<label style="display: flex; align-items: center; gap: 10px; margin-bottom: 10px;">';
            html += '<input type="checkbox" id="UploadEnabled" ' + (settings.UploadEnabled ? 'checked' : '') + '>';
            html += '<span>启用文件上传</span></label>';
            html += '<div style="margin-bottom: 10px;"><label>最大上传大小(MB): </label>';
            html += '<input type="number" id="MaxUploadSizeMB" value="' + settings.MaxUploadSizeMB + '" style="width: 100px; padding: 5px;"></div>';
            html += '<div style="margin-bottom: 10px;"><label>最大磁盘占用(%): </label>';
            html += '<input type="number" id="MaxDiskUsagePercent" value="' + settings.MaxDiskUsagePercent + '" style="width: 100px; padding: 5px;"></div>';
            html += '<label style="display: flex; align-items: center; gap: 10px; margin-bottom: 10px;">';
            html += '<input type="checkbox" id="AllowOverwrite" ' + (settings.AllowOverwrite ? 'checked' : '') + '>';
            html += '<span>允许覆盖文件</span></label>';
            html += '<label style="display: flex; align-items: center; gap: 10px;">';
            html += '<input type="checkbox" id="AllowRootUpload" ' + (settings.AllowRootUpload ? 'checked' : '') + '>';
            html += '<span>允许根目录上传</span></label>';
            html += '</fieldset>';

            html += '<fieldset style="border: 1px solid #dee2e6; padding: 15px; border-radius: 4px;">';
            html += '<legend style="font-weight: bold; padding: 0 10px;">文件预览与下载</legend>';
            html += '<label style="display: flex; align-items: center; gap: 10px; margin-bottom: 10px;">';
            html += '<input type="checkbox" id="PreviewEnabled" ' + (settings.PreviewEnabled ? 'checked' : '') + '>';
            html += '<span>启用文件预览</span></label>';
            html += '<div><label>最大下载大小(MB): </label>';
            html += '<input type="number" id="MaxDownloadSizeMB" value="' + settings.MaxDownloadSizeMB + '" style="width: 100px; padding: 5px;"></div>';
            html += '</fieldset>';

            html += '<fieldset style="border: 1px solid #dee2e6; padding: 15px; border-radius: 4px;">';
            html += '<legend style="font-weight: bold; padding: 0 10px;">网络设置</legend>';
            html += '<label style="display: flex; align-items: center; gap: 10px; margin-bottom: 10px;">';
            html += '<input type="checkbox" id="HttpEnabled" ' + (settings.HttpEnabled ? 'checked' : '') + '>';
            html += '<span>启用 HTTP</span></label>';
            html += '<label style="display: flex; align-items: center; gap: 10px; margin-bottom: 10px;">';
            html += '<input type="checkbox" id="HttpsEnabled" ' + (settings.HttpsEnabled ? 'checked' : '') + '>';
            html += '<span>启用 HTTPS</span></label>';
            html += '<label style="display: flex; align-items: center; gap: 10px;">';
            html += '<input type="checkbox" id="HttpsRedirectEnabled" ' + (settings.HttpsRedirectEnabled ? 'checked' : '') + '>';
            html += '<span>启用 HTTPS 重定向</span></label>';
            html += '</fieldset>';

            html += '<fieldset style="border: 1px solid #dee2e6; padding: 15px; border-radius: 4px;">';
            html += '<legend style="font-weight: bold; padding: 0 10px;">系统设置</legend>';
            html += '<label style="display: flex; align-items: center; gap: 10px; margin-bottom: 10px;">';
            html += '<input type="checkbox" id="Notification" ' + (settings.Notification ? 'checked' : '') + '>';
            html += '<span>启用系统通知(Windows)</span></label>';
            html += '<label style="display: flex; align-items: center; gap: 10px; margin-bottom: 10px;">';
            html += '<input type="checkbox" id="AutoRestart" ' + (settings.AutoRestart ? 'checked' : '') + '>';
            html += '<span>启用自动重启</span></label>';
            html += '<div><label>最大重启次数: </label>';
            html += '<input type="number" id="MaxRestartAttempts" value="' + settings.MaxRestartAttempts + '" style="width: 100px; padding: 5px;"></div>';
            html += '</fieldset>';

            html += '</div>';
            content.innerHTML = html;

            const rateLimitStatusDiv = document.getElementById('rateLimitStatus');
            const rateLimitInfo = document.getElementById('rateLimitInfo');
            const forceResumeBtn = document.getElementById('forceResumeBtn');

            if (settings.RequestThrottling) {
                rateLimitStatusDiv.style.display = 'block';
                rateLimitInfo.innerHTML = '<span style="color: #28a745;">✓ 请求限速已启用</span><br>最大请求: ' + rateLimitStatus.MaximumRequestsPerSecond + ' 次/秒, 告警阈值: ' + rateLimitStatus.AlarmRequestsPerSecond + ' 次/秒';
                forceResumeBtn.style.display = 'none';
            } else {
                rateLimitStatusDiv.style.display = 'none';
            }
        } else {
            content.innerHTML = '<p style="color: #dc3545;">加载设置失败: ' + (response.Message || '未知错误') + '</p>';
        }
    } catch (e) {
        content.innerHTML = '<p style="color: #dc3545;">加载设置失败: ' + e.message + '</p>';
    }
}

async function saveSettings() {
    const settings = {
        RequestThrottling: document.getElementById('RequestThrottling').checked,
        MaximumRequestsPerSecond: parseInt(document.getElementById('MaximumRequestsPerSecond').value) || 50,
        AlarmRequestsPerSecond: parseInt(document.getElementById('AlarmRequestsPerSecond').value) || 30,
        BlockedIPs: document.getElementById('BlockedIPs').value,
        TemporarilyBlockedIPs: document.getElementById('TemporarilyBlockedIPs').value,
        BlockDurationMinutes: parseInt(document.getElementById('BlockDurationMinutes').value) || 30,
        UploadEnabled: document.getElementById('UploadEnabled').checked,
        MaxUploadSizeMB: parseInt(document.getElementById('MaxUploadSizeMB').value) || 100,
        MaxDiskUsagePercent: parseInt(document.getElementById('MaxDiskUsagePercent').value) || 0,
        AllowOverwrite: document.getElementById('AllowOverwrite').checked,
        AllowRootUpload: document.getElementById('AllowRootUpload').checked,
        PreviewEnabled: document.getElementById('PreviewEnabled').checked,
        MaxDownloadSizeMB: parseInt(document.getElementById('MaxDownloadSizeMB').value) || 100,
        HttpEnabled: document.getElementById('HttpEnabled').checked,
        HttpsEnabled: document.getElementById('HttpsEnabled').checked,
        HttpsRedirectEnabled: document.getElementById('HttpsRedirectEnabled').checked,
        Notification: document.getElementById('Notification').checked,
        AutoRestart: document.getElementById('AutoRestart').checked,
        MaxRestartAttempts: parseInt(document.getElementById('MaxRestartAttempts').value) || 3
    };

    try {
        const response = await sendOperation('update_settings', null, null, settings);
        if (response.Success) {
            alert('设置已保存: ' + response.Message);
            loadSettings();
        } else {
            alert('保存失败: ' + (response.Message || '未知错误'));
        }
    } catch (e) {
        alert('保存失败: ' + e.message);
    }
}

async function forceResumeRateLimit() {
    if (!confirm('确定要强制恢复服务吗？')) return;

    try {
        const response = await sendOperation('force_resume_rate_limit', null, null);
        if (response.Success) {
            alert(response.Message);
            loadSettings();
        } else {
            alert('操作失败: ' + (response.Message || '未知错误'));
        }
    } catch (e) {
        alert('操作失败: ' + e.message);
    }
}

async function loadLogFiles() {
    const logsList = document.getElementById('logsList');
    logsList.innerHTML = '加载中...';

    try {
        const response = await sendOperation('get_logs', null, null);

        if (response.Success && response.Data && response.Data.files) {
            if (response.Data.files.length === 0) {
                logsList.innerHTML = '<p style="color: #6c757d; text-align: center;">暂无日志文件</p>';
            } else {
                let html = '<table style="width: 100%;"><thead><tr><th>文件名</th><th>大小</th><th>最后修改</th><th>操作</th></tr></thead><tbody>';
                response.Data.files.forEach(file => {
                    html += '<tr><td>' + escapeHtml(file.name) + '</td><td>' + escapeHtml(file.size) + '</td><td>' + escapeHtml(file.lastModified) + '</td><td><button class="action-btn" style="background: #007bff; color: white;" onclick="viewLogFile(\'' + escapeHtml(file.name) + '\')">查看</button></td></tr>';
                });
                html += '</tbody></table>';
                logsList.innerHTML = html;
            }
        } else {
            logsList.innerHTML = '<p style="color: #dc3545; text-align: center;">加载失败: ' + escapeHtml(response.Message) + '</p>';
        }
    } catch (e) {
        logsList.innerHTML = '<p style="color: #dc3545; text-align: center;">加载失败: ' + escapeHtml(e.message) + '</p>';
    }
}

async function viewLogFile(fileName) {
    const logsList = document.getElementById('logsList');
    const logContent = document.getElementById('logContent');
    const logText = document.getElementById('logText');
    const currentLogFile = document.getElementById('currentLogFile');

    logText.innerHTML = '加载中...';
    currentLogFile.textContent = fileName;
    logsList.style.display = 'none';
    logContent.style.display = 'block';

    try {
        const response = await sendOperation('get_log', fileName, null);

        if (response.Success && response.Data && response.Data.content) {
            logText.textContent = response.Data.content;
        } else {
            logText.innerHTML = '<span style="color: #dc3545;">加载失败: ' + escapeHtml(response.Message) + '</span>';
        }
    } catch (e) {
        logText.innerHTML = '<span style="color: #dc3545;">加载失败: ' + escapeHtml(e.message) + '</span>';
    }
}

function backToLogsList() {
    document.getElementById('logsList').style.display = 'block';
    document.getElementById('logContent').style.display = 'none';
}
