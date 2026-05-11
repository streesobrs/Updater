# Updater

一个功能强大的程序自动更新工具，支持ZIP全量更新、安装包更新、增量包更新，并提供完善的测试模式。

## 功能特性

- **多种更新方式**：支持 ZIP 全量更新、EXE 安装包更新、增量包更新
- **自动备份与回滚**：更新前自动备份关键文件，失败时自动回滚
- **完善的测试模式**：支持基础功能测试、性能测试、异常场景测试
- **详细日志记录**：记录更新过程的详细日志，便于排查问题
- **命令行参数支持**：灵活的命令行参数配置

## 项目结构

```
Updater/
├── .github/workflows/
│   └── build.yml          # GitHub Actions 构建配置
├── Updater/               # 主项目（控制台版本）
│   ├── Program.cs         # 核心更新逻辑
│   ├── Updater.csproj     # 项目配置
│   └── Properties/        # 发布配置
├── Updater.Console/       # GUI版本（WPF）
│   ├── MainWindow.xaml    # 主窗口界面
│   ├── MainWindow.xaml.cs # 主窗口逻辑
│   ├── Program.cs         # WPF应用入口
│   └── Updater.Console.csproj
├── version.ini            # 版本信息
└── Updater.sln            # 解决方案文件
```

## 使用方法

### 1. 普通更新模式

用于实际环境中执行程序更新。

**参数格式：**
```
Updater.exe <主程序文件名> <安装包路径> <目标目录> <是否删除安装包> <更新类型>
```

**参数说明：**
| 参数 | 说明 |
|------|------|
| 主程序文件名 | 更新完成后需要启动的主程序（如：Software.exe） |
| 安装包路径 | 更新包的完整路径（支持ZIP包、exe安装包） |
| 目标目录 | 程序安装/更新的目标文件夹 |
| 是否删除安装包 | 更新后是否删除安装包（true/false） |
| 更新类型 | zip / installer / incremental |

**示例：**
```bash
# 增量更新（保留安装包）
Updater.exe Software.exe "D:\update\0.1.0_to_0.2.0.zip" "D:\Software" false incremental

# ZIP全量更新（删除安装包）
Updater.exe App.exe "C:\temp\full_update.zip" "C:\App" true zip

# 安装包更新
Updater.exe Tool.exe "E:\setup\v2.0.exe" "E:\Tool" true installer
```

### 2. 测试模式

用于开发/测试阶段验证更新功能。

**参数格式：**
```
Updater.exe --test [--test-error 异常类型] [测试类型] [性能测试强度]
```

**参数说明：**
| 参数 | 说明 |
|------|------|
| --test | 固定前缀，标记进入测试模式 |
| --test-error | 异常场景测试类型（file-corruption / disk-full / permission-denied） |
| 测试类型 | zip（默认） / incremental |
| 性能测试强度 | 1-5的整数（数字越大测试强度越高） |

**性能测试级别：**
| 级别 | 大文件 | 小文件 | 目录深度 |
|------|--------|--------|----------|
| 1级 | 2个10MB | 50个 | 3层 |
| 2级 | 3个20MB | 100个 | 4层 |
| 3级 | 5个30MB | 300个 | 5层 |
| 4级 | 8个50MB | 500个 | 6层 |
| 5级 | 10个100MB | 1000个 | 8层 |

**示例：**
```bash
# 增量更新基础测试
Updater.exe --test incremental

# ZIP更新性能测试（强度3级）
Updater.exe --test zip 3

# 增量更新性能测试（强度5级）
Updater.exe --test incremental 5

# 文件损坏异常测试
Updater.exe --test --test-error file-corruption zip

# 磁盘空间不足异常测试
Updater.exe --test --test-error disk-full incremental
```

### 3. 帮助命令

```bash
Updater.exe --help
# 或
Updater.exe -h
```

## 更新类型说明

| 类型 | 说明 |
|------|------|
| zip | ZIP全量包更新，解压覆盖所有文件 |
| installer | EXE安装包更新，调用安装包执行 |
| incremental | 增量包更新，需包含 `_delete_list.txt` 删除列表文件 |

## 增量包格式

增量包为 ZIP 格式，需包含以下内容：
- 新增或修改的文件
- `_delete_list.txt`：需要删除的文件列表（每行一个文件路径，相对路径）

## 技术实现

- **语言**：C# (.NET)
- **框架**：支持 .NET 10.0
- **编码支持**：支持 GBK (936) 和 UTF-8
- **文件系统抽象**：使用 `IFileSystem` 接口提高可测试性

## 构建与发布

### 依赖项

项目使用以下 NuGet 包：
- UtfUnknown（编码检测）

### 构建命令

```bash
# 构建解决方案
dotnet build Updater.sln

# 发布控制台版本
dotnet publish Updater/Updater.csproj -c Release -o bin/Release/net10.0/publish

# 发布 GUI 版本
dotnet publish Updater.Console/Updater.Console.csproj -c Release -o bin/Release/net10.0/gui
```

## 版本历史

查看 `version.ini` 文件获取当前版本信息。

## 许可证

MIT License