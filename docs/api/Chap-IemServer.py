import hashlib
import os
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.backends import default_backend

class CHAPIEMServer:
    def __init__(self, users_db):
        """
        Initialize CHAP-IEM server with PIH optimization.
        
        users_db: List of dict objects with keys: id, username, password
        """
        self.login_index = {}      # SHA256(ciphertext) -> user_id
        self.user_keys = {}        # user_id -> pre-shared key K
        self.user_sessions = {}     # user_id -> current_session {current_key, next_id}
        self.user_data = {}         # user_id -> user info
        
        # Phase 1: Startup precomputation (same as CHAP)
        for user in users_db:
            user_id = user['id']
            username = user['username']
            password = user['password']
            
            # Compute pre-shared key K
            K = hashlib.sha256(password.encode()).digest()
            
            # Compute expected login ciphertext
            ciphertext = self._aes_encrypt(K, username)
            
            # Store hash for O(1) lookup
            login_hash = hashlib.sha256(ciphertext).digest()
            self.login_index[login_hash] = user_id
            
            # Store K for later use
            self.user_keys[user_id] = K
            self.user_data[user_id] = user
            
            # Initialize session state
            self.user_sessions[user_id] = {
                'current_key': None,   # Will be set to ID after login
                'next_id': None,
                'k': K                 # Keep K for potential re-auth
            }
    
    def login(self, ciphertext):
        """
        Phase 2: Runtime login with O(1) hash lookup.
        
        Returns: (success, user_id, current_id, encrypted_response)
        """
        # Compute hash of received ciphertext
        request_hash = hashlib.sha256(ciphertext).digest()
        
        # O(1) lookup
        user_id = self.login_index.get(request_hash)
        
        if user_id is None:
            return (False, None, None, None)
        
        # Verify with actual decryption
        K = self.user_keys[user_id]
        plaintext = self._aes_decrypt(K, ciphertext)
        
        if plaintext == self.user_data[user_id]['username']:
            # Login success - generate first ID (ID_1)
            # In CHAP-IEM, ID_1 becomes the encryption key for next operation
            current_id = self._generate_id()
            
            # Store session: current_key is None until first operation
            # The encryption key for the first operation is ID_1
            self.user_sessions[user_id]['current_key'] = current_id  # ID_1 as key
            self.user_sessions[user_id]['next_id'] = None
            
            # Response: OK + ID_1 encrypted with K (same as CHAP)
            response = f"OK|{current_id}"
            encrypted_response = self._aes_encrypt(K, response)
            
            return (True, user_id, current_id, encrypted_response)
        else:
            return (False, None, None, None)
    
    def operation(self, user_id, encrypted_packet):
        """
        Handle operation packet in CHAP-IEM mode.
        
        In CHAP-IEM, the encryption key is the CURRENT ID, not K.
        encrypted_packet: AES256_current_key(operation_data)
        """
        session = self.user_sessions.get(user_id)
        if session is None:
            return (False, None, "Session not found")
        
        current_key = session['current_key']
        
        if current_key is None:
            return (False, None, "No valid session key")
        
        # Decrypt with current_key (which is the current ID)
        try:
            plaintext = self._aes_decrypt(current_key, encrypted_packet)
        except Exception:
            # Decryption failed - key may be out of sync
            return (False, None, "Decryption failed, possible key mismatch")
        
        # In CHAP-IEM, the packet contains only operation data
        # (ID is implicitly known as the decryption key itself)
        operation_data = plaintext
        
        # Execute operation
        result = self._execute_operation(operation_data)
        
        # Generate new ID (ID_n+1)
        new_id = self._generate_id()
        
        # Store the new ID as the next key
        old_key = current_key
        session['current_key'] = new_id
        
        # Response: result + new_id encrypted with OLD key
        response = f"{result}|{new_id}"
        encrypted_response = self._aes_encrypt(old_key, response)
        
        return (True, encrypted_response, "OK")
    
    def reauthenticate(self, user_id):
        """
        Force re-authentication when keys are out of sync.
        
        Returns: instruction for client to re-authenticate
        """
        session = self.user_sessions.get(user_id)
        if session is None:
            return (False, None)
        
        # Clear current session
        K = session['k']
        session['current_key'] = None
        session['next_id'] = None
        
        # Return instruction to re-authenticate
        # Client must restart from login phase
        return (True, self._aes_encrypt(K, "out_of_sync"))
    
    def resync_attempt(self, user_id, encrypted_packet):
        """
        Handle potential resync attempt.
        
        In CHAP-IEM, resync is not possible due to key destruction.
        This method returns the re-authentication instruction.
        """
        session = self.user_sessions.get(user_id)
        if session is None:
            return (False, None, "Session not found")
        
        # CHAP-IEM cannot resync - force re-authentication
        K = session['k']
        instruction = self._aes_encrypt(K, "out_of_sync")
        return (False, instruction, "Key out of sync, please re-authenticate")
    
    def _aes_encrypt(self, key, plaintext):
        """AES-256-CBC encryption."""
        # Ensure key is bytes (if ID is hex string, convert)
        if isinstance(key, str):
            key = bytes.fromhex(key)
        
        iv = os.urandom(16)
        cipher = Cipher(algorithms.AES(key), modes.CBC(iv), backend=default_backend())
        encryptor = cipher.encryptor()
        
        # PKCS7 padding
        pad_len = 16 - (len(plaintext.encode()) % 16)
        padded = plaintext.encode() + bytes([pad_len] * pad_len)
        
        ciphertext = encryptor.update(padded) + encryptor.finalize()
        return iv + ciphertext
    
    def _aes_decrypt(self, key, ciphertext):
        """AES-256-CBC decryption."""
        if isinstance(key, str):
            key = bytes.fromhex(key)
        
        iv = ciphertext[:16]
        actual_ciphertext = ciphertext[16:]
        cipher = Cipher(algorithms.AES(key), modes.CBC(iv), backend=default_backend())
        decryptor = cipher.decryptor()
        
        padded = decryptor.update(actual_ciphertext) + decryptor.finalize()
        
        # Remove PKCS7 padding
        pad_len = padded[-1]
        return padded[:-pad_len].decode()
    
    def _generate_id(self):
        """Generate a new session ID (128-bit hex)."""
        return os.urandom(16).hex()
    
    def _execute_operation(self, operation_data):
        """Execute business logic operation."""
        return f"Result of: {operation_data}"
