export class CHAPClient {
  constructor(password) {
    this.K = this._sha256(password);
    this.currentId = null;
  }

  async _sha256(str) {
    const encoder = new TextEncoder();
    const data = encoder.encode(str);
    const hash = await crypto.subtle.digest('SHA-256', data);
    return new Uint8Array(hash);
  }

  async _aesCbcEncrypt(key, plaintext) {
    const iv = crypto.getRandomValues(new Uint8Array(16));
    const encoder = new TextEncoder();
    const data = encoder.encode(plaintext);
    
    const cryptoKey = await crypto.subtle.importKey('raw', key, { name: 'AES-CBC' }, false, ['encrypt']);
    const ciphertext = await crypto.subtle.encrypt({ name: 'AES-CBC', iv }, cryptoKey, data);
    
    const result = new Uint8Array(iv.length + ciphertext.byteLength);
    result.set(iv);
    result.set(new Uint8Array(ciphertext), iv.length);
    return result;
  }

  async _aesCbcDecrypt(key, ciphertextWithIv) {
    const iv = ciphertextWithIv.slice(0, 16);
    const ciphertext = ciphertextWithIv.slice(16);
    
    const cryptoKey = await crypto.subtle.importKey('raw', key, { name: 'AES-CBC' }, false, ['decrypt']);
    const decrypted = await crypto.subtle.decrypt({ name: 'AES-CBC', iv }, cryptoKey, ciphertext);
    
    return new TextDecoder().decode(decrypted);
  }

  async login(username, serverUrl) {
    const plaintext = username;
    const encrypted = await this._aesCbcEncrypt(this.K, plaintext);
    
    const response = await fetch(`${serverUrl}/chap/login`, {
      method: 'POST',
      body: encrypted,
      headers: { 'Content-Type': 'application/octet-stream' }
    });
    
    const responseData = new Uint8Array(await response.arrayBuffer());
    const decrypted = await this._aesCbcDecrypt(this.K, responseData);
    const parsed = JSON.parse(decrypted);
    
    if (parsed.success) {
      this.currentId = parsed.newId;
    }
    
    return parsed;
  }

  async operation(action, data = null, serverUrl) {
    if (!this.currentId) {
      throw new Error('Not logged in');
    }
    
    const payload = JSON.stringify({
      sessionId: this.currentId,
      action: action,
      data: data
    });
    
    const encrypted = await this._aesCbcEncrypt(this.K, payload);
    
    const response = await fetch(`${serverUrl}/chap/operation`, {
      method: 'POST',
      body: encrypted,
      headers: { 'Content-Type': 'application/octet-stream' }
    });
    
    const responseData = new Uint8Array(await response.arrayBuffer());
    const decrypted = await this._aesCbcDecrypt(this.K, responseData);
    const parsed = JSON.parse(decrypted);
    
    if (parsed.success && parsed.newId) {
      this.currentId = parsed.newId;
    } else if (parsed.newId && parsed.message === 'session_sync_required') {
      this.currentId = parsed.newId;
    }
    
    return parsed;
  }

  async reauthenticate(username, password, serverUrl) {
    this.K = await this._sha256(password);
    this.currentId = null;
    return this.login(username, serverUrl);
  }

  getCurrentId() {
    return this.currentId;
  }
}
