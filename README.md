# MyLiveChart Plugin

DIAVision 平台的实时折线图插件，用于工业视觉检测中实时显示测量数据曲线。

## 功能

- 实时折线图显示，支持拖拽平移和鼠标滚轮缩放
- 上限、下限、标准值、平均值辅助线
- 自动跟随最新数据（80% 吸附）
- CSV 数据导出（自动清理 14 天前文件）
- 内存保护（超过 10 万数据自动裁剪）

## 环境要求

- .NET 8.0
- Avalonia 11.3.2
- DIAVision 1.6.5.0

## 构建

```bash
dotnet build -c Release
```

## 部署

将编译产物 `bin/Release/net8.0/MyLiveChart.dll` 复制到：

```
C:\Users\Public\Documents\DMV-IVS\Plugin\MyLiveChart\
```

## 版本修复记录

原项目引用的 DIAVision 框架 DLL 为 v1.4.7.0（旧版），与主程序 v1.6.5.0 不兼容，导致插件加载时报版本错误。已更新引用路径至主程序安装目录。
