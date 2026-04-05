# WindowsNotifier API 文档

## 概述
WindowsNotifier 是一个轻量级的.NET类库，用于在Windows系统托盘中显示**气泡提示通知**。直接调用Windows API实现，无任何外部依赖。

## 快速开始

### 1. 引用DLL
```csharp
// 在项目中添加对 WindowsNotifier.dll 的引用
using WindowsNotifier;
```

### 2. 最简单的用法
```csharp
// 发送普通气泡通知（右下角弹出）
Notification.Send("你好，世界！");

// 发送带标题的通知
Notification.Send("提醒", "下午3点开会");

// 发送带图标的通知
Notification.Send("错误", "连接失败", NotificationIcon.Error);
```

## API参考

### 枚举：NotificationIcon
通知图标类型，对应系统托盘气泡提示的图标。

| 成员 | 值 | 描述 | 系统图标 |
|------|-----|------|----------|
| Info | 0 | 信息图标 | 蓝色圆圈 ⓘ |
| Warning | 1 | 警告图标 | 黄色三角形 ⚠ |
| Error | 2 | 错误图标 | 红色圆圈 ✕ |
| None | 3 | 无图标 | 无图标 |

### 静态类：Notification
提供最简单的静态API，自动管理托盘图标生命周期。

#### 初始化方法

| 方法 | 描述 |
|------|------|
| `Initialize()` | 手动初始化托盘图标（可选，Send会自动调用） |

#### 发送通知方法

| 方法 | 描述 |
|------|------|
| `Send(string content)` | 发送普通气泡通知 |
| `Send(string title, string content)` | 发送带标题的气泡通知 |
| `Send(string title, string content, NotificationIcon icon)` | 发送带标题和图标的通知 |
| `Send(string title, string content, NotificationIcon icon, int timeout, bool playSound)` | 发送完整配置的通知 |
| `SendAsync(...)` | 所有Send方法的异步版本 |

#### 清理方法

| 方法 | 描述 |
|------|------|
| `Cleanup()` | 清理托盘图标资源（程序退出前调用） |

### 方法详细说明

**Initialize()**
```csharp
/// <summary>
/// 手动初始化托盘图标。通常不需要调用，Send方法会自动初始化。
/// </summary>
public static void Initialize()
```

**Send(string content)**
```csharp
/// <summary>
/// 发送普通气泡通知（默认标题："通知"，默认图标：Info，显示时间：3000ms，播放声音：true）
/// </summary>
/// <param name="content">通知内容</param>
/// <example>
/// Notification.Send("文件下载完成");
/// </example>
public static void Send(string content)
```

**Send(string title, string content)**
```csharp
/// <summary>
/// 发送带标题的气泡通知（默认图标：Info，显示时间：3000ms，播放声音：true）
/// </summary>
/// <param name="title">通知标题</param>
/// <param name="content">通知内容</param>
/// <example>
/// Notification.Send("下载完成", "文件已保存到桌面");
/// </example>
public static void Send(string title, string content)
```

**Send(string title, string content, NotificationIcon icon)**
```csharp
/// <summary>
/// 发送带标题和图标的通知（默认显示时间：3000ms，播放声音：true）
/// </summary>
/// <param name="title">通知标题</param>
/// <param name="content">通知内容</param>
/// <param name="icon">通知图标类型</param>
/// <example>
/// Notification.Send("警告", "磁盘空间不足", NotificationIcon.Warning);
/// Notification.Send("错误", "连接超时", NotificationIcon.Error);
/// </example>
public static void Send(string title, string content, NotificationIcon icon)
```

**Send(string title, string content, NotificationIcon icon, int timeout, bool playSound)**
```csharp
/// <summary>
/// 发送完整配置的气泡通知
/// </summary>
/// <param name="title">通知标题</param>
/// <param name="content">通知内容</param>
/// <param name="icon">通知图标类型</param>
/// <param name="timeout">显示时间（毫秒，最小1000，最大30000）</param>
/// <param name="playSound">是否播放提示音</param>
/// <example>
/// // 显示5秒，静音
/// Notification.Send("提醒", "会议即将开始", NotificationIcon.Info, 5000, false);
/// </example>
public static void Send(string title, string content, NotificationIcon icon, int timeout, bool playSound)
```

**SendAsync 系列方法**
```csharp
/// <summary>
/// 所有Send方法的异步版本，返回Task，适用于不想阻塞主线程的场景
/// </summary>
/// <example>
/// await Notification.SendAsync("异步通知", "不会阻塞当前线程");
/// </example>
public static Task SendAsync(string content)
public static Task SendAsync(string title, string content)
public static Task SendAsync(string title, string content, NotificationIcon icon)
public static Task SendAsync(string title, string content, NotificationIcon icon, int timeout, bool playSound)
```

**Cleanup()**
```csharp
/// <summary>
/// 程序退出前调用，清理托盘图标资源
/// 建议在应用程序退出时调用
/// </summary>
/// <example>
/// AppDomain.CurrentDomain.ProcessExit += (s, e) => Notification.Cleanup();
/// // 或在Main函数末尾调用
/// Notification.Cleanup();
/// </example>
public static void Cleanup()
```

## 使用示例

### 示例1：基础用法
```csharp
using WindowsNotifier;

class Program
{
    static void Main(string[] args)
    {
        // 最简单的通知
        Notification.Send("Hello World");
        
        // 带标题的通知
        Notification.Send("提醒", "下午3点开会");
        
        // 带图标的通知
        Notification.Send("警告", "磁盘空间不足", NotificationIcon.Warning);
        Notification.Send("错误", "连接失败", NotificationIcon.Error);
        
        Console.WriteLine("按回车键退出...");
        Console.ReadLine();
        
        // 程序退出前清理
        Notification.Cleanup();
    }
}
```

### 示例2：Web应用中使用
```csharp
using WindowsNotifier;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 应用启动时初始化
Notification.Initialize();

// 在请求中间件中使用
app.Use(async (context, next) =>
{
    await next();
    
    // 记录访问
    Notification.Send("访问日志", 
        $"IP: {context.Connection.RemoteIpAddress}, 路径: {context.Request.Path}");
});

// 在API中使用
app.MapGet("/api/notify", (string msg) =>
{
    Notification.Send("API通知", msg);
    return Results.Ok("通知已发送");
});

// 应用退出时清理
AppDomain.CurrentDomain.ProcessExit += (s, e) => Notification.Cleanup();

app.Run();
```

### 示例3：异步使用
```csharp
using WindowsNotifier;

class Program
{
    static async Task Main(string[] args)
    {
        // 异步发送，不阻塞
        await Notification.SendAsync("正在处理中...");
        
        // 模拟耗时操作
        await Task.Delay(2000);
        
        // 发送完成通知
        await Notification.SendAsync("完成", "操作已完成", NotificationIcon.Info);
        
        Notification.Cleanup();
    }
}
```

### 示例4：控制台交互程序
```csharp
using WindowsNotifier;

class Program
{
    static void Main()
    {
        Console.WriteLine("气泡通知测试程序");
        Console.WriteLine("命令格式：");
        Console.WriteLine("  [内容] - 普通通知");
        Console.WriteLine("  info:[内容] - 信息通知");
        Console.WriteLine("  warn:[内容] - 警告通知");
        Console.WriteLine("  error:[内容] - 错误通知");
        Console.WriteLine("  exit - 退出");
        
        while (true)
        {
            Console.Write("\n输入: ");
            string input = Console.ReadLine();
            
            if (input?.ToLower() == "exit")
                break;
                
            if (input?.StartsWith("info:") == true)
                Notification.Send("信息", input[5..], NotificationIcon.Info);
            else if (input?.StartsWith("warn:") == true)
                Notification.Send("警告", input[5..], NotificationIcon.Warning);
            else if (input?.StartsWith("error:") == true)
                Notification.Send("错误", input[6..], NotificationIcon.Error);
            else
                Notification.Send("输入内容", input);
        }
        
        Notification.Cleanup();
    }
}
```

## 环境自适应

类库会自动检测运行环境并选择合适的输出方式：

| 环境 | 行为 |
|------|------|
| Windows桌面（有用户界面） | ✅ 显示系统托盘气泡通知 |
| Windows服务器（无用户界面） | 📝 自动降级为控制台输出 |
| Linux/Mac | 📝 自动降级为控制台输出 |
| 发生错误时 | 📝 自动降级为控制台输出 |

## 注意事项

1. **线程安全**：所有方法都是线程安全的
2. **自动初始化**：第一次调用Send时会自动初始化托盘图标
3. **资源清理**：程序退出前建议调用`Cleanup()`移除托盘图标
4. **显示时间**：系统可能忽略小于系统最小值的timeout参数（通常最小1000ms）
5. **重复图标**：多次初始化不会创建多个图标

## 错误处理

类库内部包含完整的错误处理，所有异常都会被捕获并降级为控制台输出，不会导致程序崩溃。

```csharp
// 即使传null也不会崩溃
Notification.Send(null); // 输出到控制台

// 空内容不会发送
Notification.Send("", ""); // 忽略
```

## 编译说明

```bash
# 编译生成DLL
dotnet build -c Release

# 输出文件位置
# WindowsNotifier/bin/Release/net10.0/WindowsNotifier.dll
```

## 版本信息

| 项目 | 信息 |
|------|------|
| 当前版本 | 1.0.0 |
| 目标框架 | .NET 10.0 |
| 依赖项 | 无（纯Windows API） |
| 平台支持 | Windows (Linux/Mac自动降级) |
| 文件大小 | ~15KB |

## 更新日志

### v1.0.0 (2024-03-13)
- ✨ 初始版本发布
- 🎯 支持系统托盘气泡通知
- 🎨 三种通知图标：Info、Warning、Error
- ⚡ 异步发送支持
- 🔧 自动环境检测和降级
- 🛡️ 完整的错误处理