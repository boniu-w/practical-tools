# Magick.NET-Q8-AnyCPU 14.17.2

用于 AVIF 预览解码，不用于其他图片格式。

- 上游：<https://github.com/dlemstra/Magick.NET>
- NuGet：`Magick.NET-Q8-AnyCPU` 14.17.2
- 许可：Magick.NET 为 Apache-2.0；随包原生库包含 ImageMagick 7.1.2-32（ImageMagick License）、aom 3.15.1 及其他第三方组件。完整声明见同目录 [Notice.txt](Notice.txt)（复制自 NuGet 包）。
- 部署：NuGet 包按 RID 提供 `Magick.Native-Q8-*`；自包含发布会把目标 RID 的原生库复制到发布根目录。
- 预览侧限制：显式 `MagickFormat.Avif`，只读首帧；设置内存、磁盘（0）、单次分配、宽高、面积和线程上限，避免解压炸弹和磁盘暂存。
