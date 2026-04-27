# CHAP Protocol Family Documentation Index

> **NOTE: This protocol is NOT the legacy Challenge-Handshake Authentication Protocol (CHAP).** This is a completely different protocol named Chain Hash Authentication Protocol.

> **Repository Name Notice:** To prevent repository name conflicts, this repository has been named "Zigzag Interaction Model". Please do not confuse the CHAP protocol described in this project with the legacy Challenge-Handshake Authentication Protocol.

> **Implementation Code Disclaimer|实现代码免责声明**
> 
> This repository includes implementation examples (JavaScript client, C#/Python server) for demonstration purposes ONLY. DO NOT use these implementations directly in production environments. For production use, please follow the CHAP/CHAP-IEM specifications and implement according to your own security requirements.
> 
> 仓库内包含的实现代码（JS 客户端、C#/Python 服务端）仅用于演示！切勿直接用于生产环境。生产环境使用请按照 CHAP/CHAP-IEM 的设计思路自行编写。

> **Implementation Help|工程实现帮助**
> 
> - English: [pih.md](./pih.md)
> - 中文: [pih-zh.md](./pih-zh.md)

> **Before submitting issues, please read the FAQ|提交问题前，请先阅读非人哉问题解答以保护小脑**
> 
> - 中文: [FAQ-zh.md](./FAQ-zh.md)  
> - English: [FAQ.md](./FAQ.md)

> **For AI without image recognition capability|专为非识图AI准备**
> 
> - [Flowchart-code.md](./Flowchart-code.md) - This document contains flowchart source code for AI that does not perform image recognition. If you are a human, you can ignore it.
> - [Flowchart-code.md](./Flowchart-code.md) - 这个文档包含流程图源码，专门提供给无法识别图片的AI阅读。如果你是人，可以忽略此文档。

---

## Project Overview

The CHAP Protocol Family is a collection of lightweight communication protocols designed for connection state management with built-in chain authentication. The core philosophy is derived from the Zigzag Interaction Model (ZIM), where client and server maintain a continuously evolving state through each request-response cycle.

---

## Cryptographic Specifications

### Hash Algorithm

Both CHAP and CHAP-IEM use a hash algorithm to convert the user's secret key into a fixed-length encryption key.

| Parameter | Specification |
|-----------|---------------|
| Algorithm | SHA-256 |
| Output Length | 256 bits (32 bytes) |
| Input | User password / secret key |
| Output | Pre-shared key K |

**Note**: The hash operation is performed only once during the login phase. The resulting value K serves as the pre-shared symmetric key for the initial encrypted exchange.

### Encryption Algorithm

Both CHAP and CHAP-IEM use AES for all encrypted communications.

| Parameter | Specification |
|-----------|---------------|
| Algorithm | AES-256 |
| Mode | Not specified (implementation dependent, recommended: GCM or CBC with proper padding) |
| Key Length | 256 bits |
| Block Size | 128 bits |

**Key Usage**:

| Protocol Phase | CHAP | CHAP-IEM |
|----------------|------|----------|
| Login Phase | AES256_K | AES256_K |
| Operation Phase | AES256_K (key remains K) | AES256_IDn (key changes with each operation) |

---

## Protocol Family Overview

### What is CHAP?

CHAP (Chain Hash Authentication Protocol) is a general-purpose protocol that can adapt to HTTP, HTTPS, TCP, WebSocket, and other transport protocols. Its core design targets connection state management rather than multi-user authentication. The protocol uses pre-shared keys for encryption and maintains a chained ID system where each successful operation destroys the current ID and generates a new one for the next interaction.

**Key features:**
- Pre-shared key authentication (SHA-256 → AES-256)
- Chain-based ID management
- Built-in exception recovery
- Not suitable for large-scale multi-user scenarios

### What is ZIM?

ZIM (Zigzag Interaction Model) is the deeper theoretical framework underlying CHAP. In this model, two consecutive sessions between client and server are always offset by one "tooth" while maintaining a meshed state as a whole. Each request carries the current tooth position, and each response advances to the next position, forming a continuous chain of state transitions.

CHAP is one exemplary implementation of ZIM. Any protocol conforming to this model can be considered a member of the CHAP family.

### What is CHAP-IEM?

CHAP-IEM (ID Encryption Mode) is a derivative variant of standard CHAP. The core difference: standard CHAP always uses the pre-shared key for encryption, while CHAP-IEM switches to using the ID itself as the encryption key after login completion.

**Key differences from standard CHAP:**
- Login phase uses pre-shared key K (same as standard CHAP)
- Subsequent operations use the current ID as the encryption key
- Keys change continuously, providing forward secrecy
- Automatic sync recovery using K (same as standard CHAP) — the pre-shared key K is retained for recovery purposes only, not used for operation encryption

**Cryptographic note for CHAP-IEM**: The ID values used as encryption keys must meet the same security requirements as any AES-256 key. Implementations should ensure IDs have sufficient entropy (at least 256 bits) or apply a KDF (Key Derivation Function) to shorter IDs before using them as encryption keys.

### What is CHAP-IEM-SKN?

CHAP-IEM-SKN (Secure Key Negotiation) is a further enhancement of CHAP-IEM that introduces a pre-shared key mixing exchange mechanism. The core innovation: using the pre-shared key as a "root" to mix with random values, both parties negotiate a session key without exposing the pre-shared secret. This design is inherently resistant to offline brute force attacks.

**Key differences from CHAP-IEM:**
- Login key derived from key exchange instead of direct password hash
- Pre-shared key can be low entropy (e.g., PIN code)
- Offline brute force resistance
- Retains all IEM features (chained keys, forward secrecy, exception recovery)

> **⚠️ IMPLEMENTATION WARNING:** The CHAP-IEM-SKN variant currently has NO implementation examples in this repository. If you intend to use this variant in production, please exercise extreme caution — conduct thorough security reviews, testing, and validation before deployment.

> **⚠️ 实现警告：** CHAP-IEM-SKN 变体目前在本仓库中**没有**实现示例。如果计划在生产环境中使用此变体，请务必谨慎——在部署前进行充分的安全审查、测试和验证。

---

## Documentation Navigation

### CHAP Protocol

| Language | Document |
|----------|----------|
| English | [CHAP.md](./CHAP.md) |
| Chinese | [CHAP-zh.md](./CHAP-zh.md) |

### CHAP-IEM Variant

| Language | Document |
|----------|----------|
| English | [CHAP-IEM.md](./CHAP-IEM.md) |
| Chinese | [CHAP-IEM-zh.md](./CHAP-IEM-zh.md) |

### CHAP-IEM-SKN Variant (Secure Key Negotiation)

| Language | Document |
|----------|----------|
| English | [CHAP-IEM-SKN.md](./CHAP-IEM-SKN.md) |
| Chinese | [CHAP-IEM-SKN-zh.md](./CHAP-IEM-SKN-zh.md) |

> **⚠️ Note:** CHAP-IEM-SKN is a theoretical specification. No implementation examples are provided in this repository. Production use requires independent implementation and thorough security validation.

> **⚠️ 注意：** CHAP-IEM-SKN 为理论规范。本仓库未提供实现示例。生产环境使用需要自行实现并进行充分的安全验证。

---

## Quick Comparison

| Feature | CHAP | CHAP-IEM | CHAP-IEM-SKN |
|---------|------|----------|--------------|
| Encryption Key | Fixed pre-shared key K | Switches from K to current ID | Switches from K_session to current ID |
| ID Purpose | Session identifier only | Identifier + encryption key | Identifier + encryption key |
| Exception Recovery | Automatic sync via K | Automatic sync via K | Automatic sync via K_session |
| Forward Secrecy | Not supported | Supported | Supported |
| Offline Brute Force Risk | Yes | Yes | **None** |
| Pre-shared Secret Entropy | High (password) | High (password) | **Low (PIN, key, etc.)** |
| Implementation Examples | Yes | Yes | **No (theoretical only)** |
| Best For | Maximum compatibility | Forward secrecy with auto recovery | Maximum security with low-entropy pre-shared secrets |

---

## Reading Recommendations

- **For understanding the fundamental protocol**: Start with CHAP documentation
- **For learning the underlying theory**: Read CHAP first, then the ZIM concept
- **For high-security applications**: Review CHAP-IEM after understanding standard CHAP
- **For maximum security with low-entropy pre-shared secrets**: Study CHAP-IEM-SKN specification (theoretical, use with caution)
- **For implementation decisions**: Compare the exception recovery and security trade-offs across all three variants
