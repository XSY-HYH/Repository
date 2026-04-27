export class CHAP_IEM_Client {
  constructor(password) {
    this.K = null;
    this.currentKey = null;
    this.currentId = null;
    this._initKey(password);
  }

  async _initKey(password) {
    const encoder = new TextEncoder();
    const data = encoder.encode(password);
    const hash = await crypto.subtle.digest('SHA-256', data);
    this.K = new Uint8Array(hash);
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

  async _ensureKeyIs256Bits(key) {
    if (key.length === 32) return key;
    if (key.length === 16) {
      const hash = await this._sha256(new TextDecoder().decode(key));
      return hash;
    }
    if (typeof key === 'string') {
      const hash = await this._sha256(key);
      return hash;
    }
    return key;
  }

  async login(username, serverUrl) {
    if (!this.K) {
      throw new Error('Key not initialized');
    }
    
    const plaintext = username;
    const encrypted = await this._aesCbcEncrypt(this.K, plaintext);
    
    const response = await fetch(`${serverUrl}/chap-iem/login`, {
      method: 'POST',
      body: encrypted,
      headers: { 'Content-Type': 'application/octet-stream' }
    });
    
    const responseData = new Uint8Array(await response.arrayBuffer());
    const decrypted = await this._aesCbcDecrypt(this.K, responseData);
    const parsed = JSON.parse(decrypted);
    
    if (parsed.success && parsed.newId) {
      this.currentId = parsed.newId;
      this.currentKey = await this._ensureKeyIs256Bits(this.currentId);
    }
    
    return parsed;
  }

  async operation(action, data = null, serverUrl) {
    if (!this.currentId || !this.currentKey) {
      throw new Error('Not logged in or session expired');
    }
    
    const payload = JSON.stringify({
      sessionId: this.currentId,
      action: action,
      data: data
    });
    
    const encrypted = await this._aesCbcEncrypt(this.currentKey, payload);
    
    const response = await fetch(`${serverUrl}/chap-iem/operation`, {
      method: 'POST',
      body: encrypted,
      headers: { 'Content-Type': 'application/octet-stream' }
    });
    
    const responseData = new Uint8Array(await response.arrayBuffer());
    const decrypted = await this._aesCbcDecrypt(this.currentKey, responseData);
    const parsed = JSON.parse(decrypted);
    
    if (parsed.success && parsed.newId) {
      this.currentId = parsed.newId;
      this.currentKey = await this._ensureKeyIs256Bits(this.currentId);
    } else if (parsed.message === 'out_of_sync' || parsed.message === 'session_expired') {
      this.currentId = null;
      this.currentKey = null;
    }
    
    return parsed;
  }

  async reauthenticate(username, password, serverUrl) {
    await this._initKey(password);
    this.currentId = null;
    this.currentKey = null;
    return this.login(username, serverUrl);
  }

  getCurrentId() {
    return this.currentId;
  }

  isAuthenticated() {
    return this.currentId !== null && this.currentKey !== null;
  }
}
