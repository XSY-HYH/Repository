function escapeHtml(text) {
    if (text == null) return '';
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

function getCurrentPath() {
    const urlParams = new URLSearchParams(window.location.search);
    let path = urlParams.get('path');
    
    if (!path) {
        path = window.location.pathname;
        
        if (path.startsWith('/')) {
            path = path.substring(1);
        }
        if (path.endsWith('/') && path.length > 1) {
            path = path.substring(0, path.length - 1);
        }
        
        try {
            path = decodeURIComponent(path);
        } catch (err) {
            console.warn('路径解码失败:', path);
        }
    }
    
    return path || '';
}

function loadFileList() {
    const currentPath = getCurrentPath();
    
    const titleElement = document.querySelector('h1');
    titleElement.textContent = 'Directory listing for /' + (currentPath || '');
    
    let apiUrl = '/api/files';
    if (currentPath) {
        apiUrl += '?path=' + encodeURIComponent(currentPath);
    }
    
    fetch(apiUrl)
        .then(response => {
            if (!response.ok) {
                throw new Error('HTTP状态码: ' + response.status);
            }
            return response.json();
        })
        .then(data => {
            const tbody = document.getElementById('fileList');
            
            tbody.innerHTML = '';
            
            const isEmpty = (data.directories.length === 0 && data.files.length === 0);
            if (isEmpty) {
                tbody.innerHTML = '<tr><td colspan="3" class="empty">空目录</td></tr>';
                return;
            }
           
            if (currentPath) {
                const row = document.createElement('tr');
                let parentPath = currentPath.substring(0, currentPath.lastIndexOf('/'));
                let parentUrl = '/?';
                if (parentPath) {
                    parentUrl += 'path=' + encodeURIComponent(parentPath);
                }
                
                row.innerHTML = `
                    <td><a href="${escapeHtml(parentUrl)}" class="dir">../</a></td>
                    <td class="date">-</td>
                    <td class="size">-</td>
                `;
                tbody.appendChild(row);
            }
            
            if (data.directories.length > 0) {
                data.directories.forEach(dir => {
                    const row = document.createElement('tr');
                    
                    let dirUrl = '/?';
                    if (currentPath) {
                        dirUrl += 'path=' + encodeURIComponent(currentPath + '/' + dir.name);
                    } else {
                        dirUrl += 'path=' + encodeURIComponent(dir.name);
                    }
                    
                    row.innerHTML = `
                        <td><a href="${escapeHtml(dirUrl)}" class="dir">${escapeHtml(dir.name)}/</a></td>
                        <td class="date">${escapeHtml(dir.lastModified)}</td>
                        <td class="size">-</td>
                    `;
                    tbody.appendChild(row);
                });
            }
            
            if (data.files.length > 0) {
                data.files.forEach(file => {
                    const row = document.createElement('tr');
                    const fileSize = file.size ? formatFileSize(file.size) : '-';
                    
                 
                    const getDownloadUrl = async (filePath) => {
                        try {
                            // 使用当前时间戳作为缓存破坏参数
                            const timestamp = Date.now();
                          
                            return `/api/download/${encodeURIComponent(filePath)}?_t=${timestamp}`;
                                                                                //这里会加时间戳
                        } catch (error) {
                            console.error('获取下载链接失败:', error);
                            return null;
                        }
                    };
                    
                    const link = document.createElement('a');
                    link.href = '#';
                    link.className = 'file';
                    link.textContent = file.name;
                    
                    // 点击下载链接时强制下载并添加缓存破坏参数
                    link.onclick = async function(e) {
                        e.preventDefault();
                        const downloadUrl = await getDownloadUrl(file.url);
                        if (downloadUrl) {
                            // 创建iframe（隐藏）来触发下载，确保不会缓存
                            const iframe = document.createElement('iframe');
                            iframe.style.display = 'none';
                            iframe.src = downloadUrl;
                            document.body.appendChild(iframe);
                            
                            // 移除iframe
                            setTimeout(() => {
                                document.body.removeChild(iframe);
                            }, 5000);
                        } else {
                            alert('获取下载链接失败，请稍后重试');
                        }
                    };
                    
                    const copyButton = document.createElement('button');
                    copyButton.className = 'copy-btn';
                    copyButton.textContent = '📋';
                    copyButton.title = '复制下载链接';
                    copyButton.onclick = async function() {
                        const downloadUrl = await getDownloadUrl(file.url);
                        if (downloadUrl) {
                            copyDownloadLink(downloadUrl, this);
                        } else {
                            // 显示复制失败状态
                            const originalText = this.textContent;
                            this.textContent = '❌';
                            this.style.background = '#dc3545';
                            setTimeout(() => {
                                this.textContent = originalText;
                                this.style.background = '#28a745';
                            }, 2000);
                        }
                    };
                    
                    const nameCell = document.createElement('td');
                    nameCell.appendChild(link);
                    
                    if (file.previewable) {
                        const previewButton = document.createElement('button');
                        previewButton.className = 'preview-btn';
                        previewButton.textContent = '预览';
                        previewButton.title = '预览文件';
                        previewButton.onclick = function() {
                            previewFile(file.url, file.name);
                        };
                        nameCell.appendChild(previewButton);
                    }
                    nameCell.appendChild(copyButton);
                    
                    const dateCell = document.createElement('td');
                    dateCell.className = 'date';
                    dateCell.textContent = file.lastModified;
                    
                    const sizeCell = document.createElement('td');
                    sizeCell.className = 'size';
                    sizeCell.textContent = fileSize;
                    
                    row.appendChild(nameCell);
                    row.appendChild(dateCell);
                    row.appendChild(sizeCell);
                    tbody.appendChild(row);
                });
            }
        })
        .catch(error => {
            console.error('文件列表加载失败:', error);
            
            const tbody = document.getElementById('fileList');
            tbody.innerHTML = '<tr><td colspan="3" class="empty">加载失败，请稍后重试</td></tr>';
        });
}

function copyDownloadLink(url, buttonElement) {
    const fullUrl = window.location.origin + url;
    
    function updateButtonStatus(isSuccess) {
        if (!buttonElement) return;
        
        const originalText = buttonElement.textContent;
        buttonElement.textContent = isSuccess ? '✅' : '❌';
        buttonElement.style.background = isSuccess ? '#198754' : '#dc3545';
        
        setTimeout(() => {
            if (buttonElement) {
                buttonElement.textContent = originalText;
                buttonElement.style.background = '#28a745';
            }
        }, 2000);
    }
    
    if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(fullUrl)
            .then(function() {
                updateButtonStatus(true);
            })
            .catch(function() {
                fallbackCopyText(fullUrl);
            });
    } else {
        fallbackCopyText(fullUrl);
    }
    
    function fallbackCopyText(text) {
        try {
            const textarea = document.createElement('textarea');
            textarea.value = text;
            textarea.style.position = 'fixed';
            textarea.style.left = '-999999px';
            textarea.style.top = '-999999px';
            document.body.appendChild(textarea);
            
            textarea.focus();
            textarea.select();
            
            const successful = document.execCommand('copy');
            
            document.body.removeChild(textarea);
            
            updateButtonStatus(successful);
        } catch (err) {
            console.error('复制失败:', err);
            updateButtonStatus(false);
        }
    }
}

function previewFile(fileUrl, fileName) {
    const modal = document.getElementById('previewModal');
    const title = document.getElementById('previewFileName');
    const content = document.getElementById('previewContent');
    const loading = document.getElementById('previewLoading');
    const error = document.getElementById('previewError');
    const closeBtn = document.querySelector('.close-btn');
    const closePreviewBtn = document.getElementById('closePreviewBtn');

    modal.style.display = 'block';

    title.textContent = fileName;

    loading.style.display = 'block';
    content.style.display = 'none';
    error.style.display = 'none';

    const previewApiUrl = '/api/preview/' + encodeURIComponent(fileUrl);

    let objectUrl = null;

    const closeModal = function() {
        if (objectUrl) {
            URL.revokeObjectURL(objectUrl);
            objectUrl = null;
        }
        modal.style.display = 'none';
    };

    if (closeBtn) closeBtn.onclick = closeModal;
    if (closePreviewBtn) closePreviewBtn.onclick = closeModal;

    window.onclick = function(event) {
        if (event.target === modal) {
            closeModal();
        }
    };

    fetch(previewApiUrl)
        .then(response => {
            if (!response.ok) {
                throw new Error('HTTP状态码: ' + response.status);
            }

            const contentType = response.headers.get('content-type');

            if (contentType && contentType.includes('application/json')) {
                return response.json();
            }

            return response.blob().then(blob => {
                return { isBinary: true, blob: blob, contentType: contentType };
            });
        })
        .then(data => {
            loading.style.display = 'none';

            content.style.display = 'block';

            if (data.isBinary) {
                content.innerHTML = '';

                objectUrl = URL.createObjectURL(data.blob);

                if (data.contentType && data.contentType.startsWith('image/')) {
                    const img = document.createElement('img');
                    img.src = objectUrl;
                    img.style.maxWidth = '100%';
                    img.style.maxHeight = '70vh';
                    img.style.display = 'block';
                    img.style.margin = '0 auto';
                    content.appendChild(img);
                } else if (data.contentType && data.contentType.startsWith('audio/')) {
                    const audio = document.createElement('audio');
                    audio.src = objectUrl;
                    audio.controls = true;
                    audio.style.width = '100%';
                    content.appendChild(audio);
                } else if (data.contentType && data.contentType.startsWith('video/')) {
                    const video = document.createElement('video');
                    video.src = objectUrl;
                    video.controls = true;
                    video.style.maxWidth = '100%';
                    video.style.maxHeight = '70vh';
                    content.appendChild(video);
                } else {
                    const a = document.createElement('a');
                    a.href = objectUrl;
                    a.download = fileName;
                    a.textContent = '下载文件';
                    a.className = 'btn btn-primary';
                    content.appendChild(a);
                }

                return;
            }

            if (data.content !== undefined) {
                content.textContent = data.content;
            }
        })
        .catch(err => {
            console.error('预览文件失败:', err);

            loading.style.display = 'none';

            error.style.display = 'block';
            error.textContent = '预览失败: ' + (err.message || '未知错误');
        });
}