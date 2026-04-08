CHAP（Chain Hash Authentication Protocol）协议
其本质是一个通用协议，理论可以适配http https TCP Web socket
该协议的核心流程其实很好记，首先，客户端需要持有和服务端相同的密钥，在连接时，客户端需要填写用户名和密钥本身，客户端使用密钥换成哈希值使用，哈希值再经过aes加密，用户名本身，然后发送给服务端，由于客户端和服务端持有同一个密钥，且使用对称加密，服务端可以使用预配置的密钥解密该数据包，如果解密失败或用户名不匹配则过滤，验证成功后会返回操作结果为OK，同时携带了ID，这个ID非常重要，客户端下一次操作必须持有该ID来操作，否则会被服务端抛弃，这里假设登录成功返回的ID是一，那么，客户端下一次数据包就必须使用密钥去加密数据包，同时包含操作ID为一以及操作数据，服务端会使用密钥解密并匹配操作ID销毁ID 1，同时生成ID 2，把ID 2和结果打包加密返回给客户端，以此类推便是chap完整协议的流程
其核心是为了管理设计而并非多用户，用户增多会增加服务端解密成本，这是chap不适合的场景
Chap也具备拓展性，其最初版本是不适合多人用户的，但衍生版本比如chap_iem（ID Encryption Mode）理论上可以适配


CHAP流程图
sequenceDiagram
    participant C as 客户端
    participant S as 服务端
    participant A as 攻击者(窃听/重放)

    Note over C: 用户输入密码<br/>计算 hash(密码) = 密钥K

    rect rgb(128, 128, 128)
        Note over C,S: 【登录阶段】
        C->>S: ① 登录包 = AES256_K(用户名)
        Note right of A: 截获密文<br/>无K → 无法解密
        
        S->>S: 用K解密<br/>验证用户名
        alt 解密失败或用户名无效
            S-->>C: ❌ 报错断开
        end
        
        S->>C: ② 响应包 = AES256_K(登录成功 + ID_1)
        Note right of A: 截获密文<br/>无K → 读不出ID_1
        
        C->>C: 解密得到 ID_1<br/>持有密钥K + ID_1
    end

    rect rgb(128, 128, 128)
        Note over C,S: 【第一次操作 - 正常流程】
        C->>S: ③ 操作包 = AES256_K(操作指令 + ID_1)
        
        S->>S: 解密成功 ✓<br/>校验ID_1有效 ✓<br/>执行操作<br/>销毁ID_1<br/>生成ID_2
        
        S->>C: ④ 响应包 = AES256_K(操作结果 + ID_2)
        
        C->>C: 解密得到 ID_2<br/>更新本地ID为ID_2
    end

    rect rgb(128, 128, 128)
        Note over C,S: 【第二次操作 - 正常流程】
        C->>S: ⑤ 操作包 = AES256_K(操作指令 + ID_2)
        
        S->>S: 解密成功 ✓<br/>校验ID_2有效 ✓<br/>执行操作<br/>销毁ID_2<br/>生成ID_3
        
        S->>C: ⑥ 响应包 = AES256_K(操作结果 + ID_3)
        
        C->>C: 解密得到 ID_3<br/>更新本地ID为ID_3
    end

    rect rgb(255, 255, 150)
        Note over C,S: 【异常场景：响应包丢失，客户端ID不同步】
        C->>S: ⑦ 操作包 = AES256_K(操作指令 + ID_3)
        
        S->>S: 解密成功 ✓<br/>校验ID_3有效 ✓<br/>执行操作<br/>销毁ID_3<br/>生成ID_4
        
        S->>C: ⑧ 响应包 = AES256_K(操作结果 + ID_4)
        Note over S,C: ❌ 网络故障，响应包丢失
        
        C->>C: 未收到响应<br/>本地ID仍为ID_3
        
        Note over C: 用户再次点击操作
        C->>S: ⑨ 操作包 = AES256_K(操作指令 + ID_3)
        
        S->>S: 解密成功 ✓<br/>校验ID_3 → 已失效 ❌<br/>（因为ID_3已被销毁，ID_4是当前有效ID）
        
        Note over S: 【自动恢复逻辑】
        S->>C: ⑩ 恢复包 = AES256_K("resync" + ID_4 + "请更新本地ID")
        
        C->>C: 解密得到 ID_4<br/>更新本地ID为ID_4
        C->>S: ⑪ 确认包 = AES256_K("resync_ack" + ID_4)
        
        S->>S: 校验ID_4有效 ✓
        S->>C: ⑫ 响应包 = AES256_K("resync_ok")
        
        Note over C: 客户端ID已同步为ID_4<br/>可继续正常操作
    end

    rect rgb(70, 130, 255)
        Note over A: 【攻击者尝试重放】
        A->>S: 重放 ③ 的旧包 (含ID_1)
        S->>S: 解密成功 ✓<br/>但ID_1已失效 ❌
        S-->>A: 拒绝操作，ID无效
        
        A->>S: 发送伪造密文
        S->>S: 解密失败 (无K) ❌
        S-->>A: 拒绝，解密失败
    end

    Note over C,S: 正常操作：每次携带当前ID → 服务端销毁旧ID生成新ID → 形成链式状态<br/>异常恢复：服务端发现旧ID失效 → 返回当前有效ID → 客户端同步后继续

CHAP-IEM
sequenceDiagram
    participant C as 客户端
    participant S as 服务端
    participant A as 攻击者

    rect rgb(128, 128, 128)
        Note over C,S: 【登录阶段 (无预共享密钥)】
        C->>S: ① 登录请求 (用户名 + 临时公钥/简单握手)
        Note right of A: 仅用于身份识别，无加密材料
        S->>S: 验证用户存在
        S->>C: ② 响应包 = ID_1 (明文或简单编码)
        Note right of A: 攻击者可截获ID_1<br/>但后续通信需用此ID加密<br/>攻击者无ID_1的衍生密钥? 不对，ID_1就是密钥
        
        Note over C: 客户端收到 ID_1<br/>设置当前加密密钥 = ID_1
    end

    rect rgb(128, 128, 128)
        Note over C,S: 【第一次操作 - 正常流程 (ID作为密钥)】
        C->>C: 用 ID_1 加密操作指令
        C->>S: ③ 操作包 = AES256_ID1(操作指令)
        
        S->>S: 用 ID_1 解密 ✓<br/>执行操作<br/>生成新密钥 ID_2
        
        S->>C: ④ 响应包 = AES256_ID1(操作结果 + ID_2)
        
        C->>C: 用 ID_1 解密 ✓<br/>得到 ID_2<br/>更新当前加密密钥为 ID_2
    end

    rect rgb(128, 128, 128)
        Note over C,S: 【第二次操作 - 正常流程】
        C->>C: 用 ID_2 加密操作指令
        C->>S: ⑤ 操作包 = AES256_ID2(操作指令)
        
        S->>S: 用 ID_2 解密 ✓<br/>执行操作<br/>生成新密钥 ID_3
        
        S->>C: ⑥ 响应包 = AES256_ID2(操作结果 + ID_3)
        
        C->>C: 用 ID_2 解密 ✓<br/>得到 ID_3<br/>更新当前加密密钥为 ID_3
    end

    rect rgb(255, 255, 150)
        Note over C,S: 【异常场景：响应包丢失，密钥不同步】
        C->>C: 用 ID_3 加密操作指令
        C->>S: ⑦ 操作包 = AES256_ID3(操作指令)
        
        S->>S: 用 ID_3 解密 ✓<br/>执行操作<br/>销毁 ID_3<br/>生成新密钥 ID_4
        
        S->>C: ⑧ 响应包 = AES256_ID3(操作结果 + ID_4)
        Note over S,C: ❌ 网络故障，响应包丢失
        
        C->>C: 未收到响应<br/>本地密钥仍为 ID_3
        
        Note over C: 用户再次点击操作
        C->>C: 用 ID_3 加密操作指令
        C->>S: ⑨ 操作包 = AES256_ID3(操作指令)
        
        S->>S: 用 ID_3 解密 ✓<br/>但 ID_3 已失效 ❌<br/>（服务端当前有效密钥是 ID_4）
        
        Note over S: 【自动恢复逻辑】
        Note over S: 服务端识别出客户端密钥滞后<br/>但无法用 ID_3 加密返回 ID_4<br/>需特殊处理
        S->>C: ⑩ 恢复包 = 特殊指令 (如 "out_of_sync")<br/>要求客户端重新登录
        
        C->>S: 重新走登录流程
        Note over C: 客户端重新登录<br/>获得新 ID_1'<br/>重新开始密钥链
    end

    rect rgb(70, 130, 255)
        Note over A: 【攻击者尝试】
        A->>S: 重放 ③ 的旧包 (AES256_ID1)
        S->>S: 当前有效密钥是 ID_2 或 ID_3<br/>用它们解密旧包失败 ❌
        S-->>A: 拒绝，解密失败
        
        A->>S: 发送伪造密文
        S->>S: 解密失败 ❌
        S-->>A: 拒绝
    end

    Note over C,S: 正常操作：密钥链 ID1 → ID2 → ID3 ...<br/>每次操作后，密钥自动更换<br/>异常恢复：密钥不同步时，需重新登录

Chap代表的其实不是，一个专有框架，其更深层次其实是Zigzag Interaction Model，即锯齿形交互模型，简称zim，这种协议的根本就是客户端和服务端交互过程，或者说两次会话，交互之间永远相差一个齿，但却能保持啮合状态，Chap其实只是zim模型的一个实现典范，如果遵循该模型，其实都可以算作是chap家族