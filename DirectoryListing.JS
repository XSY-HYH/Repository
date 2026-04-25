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
            console.warn(t('path_decode_failed') + ':', path);
        }
    }
    
    return path || '';
}

function loadFileList() {
    const currentPath = getCurrentPath();
    
    const titleElement = document.querySelector('h1');
    titleElement.textContent = t('directory_for') + ' /' + (currentPath || '');
    
    let apiUrl = '/api/files';
    if (currentPath) {
        apiUrl += '?path=' + encodeURIComponent(currentPath);
    }
    
    fetch(apiUrl)
        .then(response => {
            if (!response.ok) {
                throw new Error('HTTP: ' + response.status);
            }
            return response.json();
        })
        .then(data => {
            const tbody = document.getElementById('fileList');
            
            tbody.innerHTML = '';
            
            if (data.directoryHash) {
                const titleElement = document.querySelector('h1');
                const hashSpan = document.createElement('span');
                hashSpan.className = 'directory-hash';
                hashSpan.textContent = data.directoryHash.substring(0, 8);
                hashSpan.title = data.directoryHash + '\n' + (t('click_to_copy') || '点击复制');
                hashSpan.onclick = function() {
                    copyToClipboard(data.directoryHash, this);
                };
                titleElement.appendChild(document.createTextNode(' '));
                titleElement.appendChild(hashSpan);
            }
            
            const isEmpty = (data.directories.length === 0 && data.files.length === 0);
            if (isEmpty) {
                tbody.innerHTML = '<tr><td colspan="3" class="empty">' + t('empty_directory') + '</td></tr>';
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
                            const timestamp = Date.now();
                            return `/api/download/${encodeURIComponent(filePath)}?_t=${timestamp}`;
                        } catch (error) {
                            console.error(t('get_link_failed') + ':', error);
                            return null;
                        }
                    };
                    
                    const link = document.createElement('a');
                    link.href = '#';
                    link.className = 'file';
                    link.textContent = file.name;
                    
                    link.onclick = async function(e) {
                        e.preventDefault();
                        const downloadUrl = await getDownloadUrl(file.url);
                        if (downloadUrl) {
                            const iframe = document.createElement('iframe');
                            iframe.style.display = 'none';
                            iframe.src = downloadUrl;
                            document.body.appendChild(iframe);
                            
                            setTimeout(() => {
                                document.body.removeChild(iframe);
                            }, 5000);
                        } else {
                            alert(t('get_link_failed'));
                        }
                    };
                    
                    const copyButton = document.createElement('button');
                    copyButton.className = 'copy-btn';
                    copyButton.textContent = '📋';
                    copyButton.title = t('copy_link');
                    copyButton.onclick = async function() {
                        const downloadUrl = await getDownloadUrl(file.url);
                        if (downloadUrl) {
                            copyDownloadLink(downloadUrl, this);
                        } else {
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
                        previewButton.textContent = t('preview');
                        previewButton.title = t('preview');
                        previewButton.onclick = function() {
                            previewFile(file.url, file.name);
                        };
                        nameCell.appendChild(previewButton);
                    }
                    nameCell.appendChild(copyButton);
                    
                    if (file.sha256) {
                        const sha256Span = document.createElement('span');
                        sha256Span.className = 'file-sha256';
                        sha256Span.textContent = file.sha256.substring(0, 8);
                        sha256Span.title = file.sha256 + '\n' + (t('click_to_copy') || '点击复制');
                        sha256Span.onclick = function(e) {
                            e.stopPropagation();
                            copyToClipboard(file.sha256, this);
                        };
                        nameCell.appendChild(sha256Span);
                    }
                    
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
            console.error(t('file_list_load_failed') + ':', error);
            
            const tbody = document.getElementById('fileList');
            tbody.innerHTML = '<tr><td colspan="3" class="empty">' + t('load_failed') + '</td></tr>';
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
            console.error(t('copy_failed') + ':', err);
            updateButtonStatus(false);
        }
    }
}

function copyToClipboard(text, element) {
    if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(text)
            .then(function() {
                if (element) {
                    const original = element.textContent;
                    element.textContent = '✓';
                    element.style.color = '#198754';
                    setTimeout(() => {
                        element.textContent = original;
                        element.style.color = '';
                    }, 1500);
                }
            })
            .catch(function() {
                fallbackCopy(text, element);
            });
    } else {
        fallbackCopy(text, element);
    }
}

function fallbackCopy(text, element) {
    try {
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.left = '-999999px';
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        document.body.removeChild(textarea);
        
        if (element) {
            const original = element.textContent;
            element.textContent = '✓';
            element.style.color = '#198754';
            setTimeout(() => {
                element.textContent = original;
                element.style.color = '';
            }, 1500);
        }
    } catch (err) {
        console.error(t('copy_failed') + ':', err);
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
    loading.textContent = t('loading_content');
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
                throw new Error('HTTP: ' + response.status);
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
                    a.textContent = t('download_file');
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
            console.error(t('preview_failed') + ':', err);

            loading.style.display = 'none';

            error.style.display = 'block';
            error.textContent = t('preview_failed') + ': ' + (err.message || t('unknown_error'));
        });
}
