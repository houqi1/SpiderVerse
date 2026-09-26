# Unity Object Line Art 使用说明

迁移位置：`Assets/LineArt/`。适配 Unity 6000.0.48f1 / URP 17，默认使用 RenderGraph，也提供 URP Compatibility 路径。

## 打开与调整

1. 打开 `Assets/LineArt/LineArtPreview.unity`。这是 SampleScene 的副本，角色通过 `Object Line Art` 对象上的 Source 组件指定。
2. 菜单 `SpiderVerse > Line Art > Select Stroke Settings` 选择描边设置；也可以在 `LineArt_Renderer.asset` 的 `Object Line Art` Renderer Feature 上调整。
3. 在 Texture 字段拖入纹理资源，支持 Unity 已导入的 TGA、PNG 等 Texture2D。带透明度的纹理保留 Alpha；白底黑色笔刷勾选 Dark On White Mask。可调 Texture Strength 和 Texture Repeats。
4. Length Randomness 为双向随机：0.4 对应裁剪后长度的 60%–140%。以合并后的完整可见线条为单位，两端对称缩短或按端点切线增长。Closed loop 在固定接缝处处理。随机值不随帧刷新。
5. Thickness 默认按世界尺寸投影，随角色远近一起缩放。End Taper 控制端点收细程度，Thickness Transition 控制过渡曲线。Offset 的正 X 向右、正 Y 向下。宽度、过渡、纹理和偏移直接更新材质，不重建线条。

## 在其他对象/场景使用

- 给根物体添加 `Object Line Art Source`。Renderers 留空时包含子级 MeshRenderer 和 SkinnedMeshRenderer，也可以指定列表。
- 模型导入设置需要开启 Read/Write。预览安装器自动处理角色引用的模型；纹理无需 Read/Write。
- 相机选择 `LineArt_Renderer`（预览安装器把它追加到 PC/Mobile URP Asset 的 Renderer 列表，默认 Renderer 不变）。也可以手工把 `ObjectLineArtFeature` 加入自己的 Renderer。
- Shader 字段保留 `ObjectLineArt.shader` 引用，确保构建时保留 Shader。
- `SpiderVerse > Line Art > Create or Update Preview Scene` 可以重建接线；不会覆盖已存在预览场景里的其他对象。原 SampleScene、PC_Renderer 上的原描边配置不被替换。

## 角色抽帧动画

给角色 **Animator 所在的同一个物体** 添加 `Animation > Line Art Stepped Animator`。`Reference Camera` 指向启用了 Object Line Art Feature 的相机，留空使用 Main Camera。进入 Play 后生效；无需另外设置动画帧率，直接跟随该相机 Line Art 的 `Update Rate`（预览资源当前为 12）。修改 Update Rate 后，后续更新自动跟随。

脚本在 Line Art 的共享采样代次变化时手动推进 Animator，其余帧保持姿态。累积实际动画时间，保持播放速度；支持 Animator.speed，普通模式跟随 Time.timeScale，Unscaled Time 模式使用未缩放时间。运行时 Animator 的 Enabled 会由脚本关闭以接管更新；要恢复普通播放，请关闭 **Line Art Stepped Animator** 组件。相机或 Feature 不可用时也会释放接管并给出一次提示。多个角色使用同一相机即可共享采样节奏。

此组件适用于由 Animator Controller 驱动的角色；不接管 Timeline、物理模拟或其他脚本直接写入的骨骼。Root Motion 和动画事件也随采样更新，Fixed 模式在这里按渲染采样节奏推进，不保留物理固定步同步。CPU Line Art 后台提取仍可能因计算耗时滞后于姿态。编辑模式 Animation 窗口预览不受此组件控制。

手动更新 API：[Unity Animator.Update](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator.Update.html)。

## 多层描边与 Noise Frequency

在 `Object Line Art Source > Stroke Layers` 点击 **Add Layer**。每层拥有独立的 Appearance，可复制、开关和上下排序；后面的层覆盖前面的层。取消 **Use Source Renderers** 后，可在 **Layer Renderers** 单独指定该层的对象；此时列表为空表示该层不绘制。保留勾选则沿用 Source Renderers（Source 列表为空时自动获取子级 Renderer）。

层列表为空时保持旧版单层行为，使用 Renderer Feature 的 Appearance。添加第一层会复制当前已加载、启用的 Feature 外观。存在层定义后，以这些层为准；全部关闭时不绘制。边类型、遮挡、连接和 Update Rate 仍在 Renderer Feature 中统一设置。

`Scale With Distance` 默认开启：Thickness、Offset、Random Offset 和 Noise 振幅都按世界尺寸投影，随角色远近及相机缩放保持比例。`Size Unit` 为每个外观参数单位对应的世界长度，默认 0.005；各层独立设置。正 X 向右、正 Y 向下。关闭 Scale With Distance 才使用固定渲染像素。随机偏移的随机种子仍随笔划身份保持稳定；轮廓重新连接可能改变身份。

每层 `Appearance > Noise Frequency` 可调为 0.1–32，表示整条笔划的噪声周期数，默认 3 保留原频率；Noise 控制振幅。高频噪声会自动增加条带采样，因此比低频有更多几何开销。

Thickness Curve / End Taper 仍控制笔划收尖。CPU/GPU 均按每个顶点的深度和相机投影计算尺寸；不增加提取、遮挡或绘制次数。`LineArtWidthChecks.BatchRun` 验证透视深度 2 / 20 下的宽度和偏移随投影缩放、正交相机的世界尺寸，并保留显式固定像素模式的回归检查。

GPU 每个相机只维护一份目标并集的拓扑、蒙皮输入、BVH、候选边、遮挡结果和连接链。Renderer 集合与长度/采样参数一致的层共享同一份条带缓冲；颜色、宽度、收尖、纹理和偏移使用独立 MaterialPropertyBlock。不同 Renderer 集合、裁剪长度或采样密度只重建相应条带，不重复整套提取与遮挡。CPU 回退也复用提取结果，并按相同条件共享 Mesh。每层仍有独立绘制成本。

2026-09-22，RTX 4060 Laptop / D3D11 / 960×960 动画场景离屏测试：单层两轮平均帧耗时 1.360 / 1.440 ms；三层共享条带 1.417 ms；三层独立长度 1.592 ms；三层分别指定 Renderer 子集 1.459 ms。三层共享测试确认为 `3 layers / 1 stroke sets`。这些是整个离屏场景的有限采样，不能当作编辑器 FPS 或精确 GPU 阶段时间。

新增检查覆盖实际像素颜色与层顺序、Renderer 隔离、交线双侧归属、关闭/空选择、噪声频率采样、GPU 缓冲与 CPU Mesh 复用，以及旧资源默认值。日志：`Library/LineArtValidation/layers-check4-build.log`；基准与截图：`Library/LineArtValidation/Validation/layers-*.txt/.png`。命令行基准可加 `-lineArtLayers 3 -lineArtLayerVariant uniform`，另支持 `shape` 和 `selection`。

## CPU Reference 实现与性能

- 主线程获取静态网格/蒙皮 BakeMesh 快照；焊接拓扑按 Mesh 缓存。
- 后台 Task 做轮廓/折痕/材质边界/三角形交线提取，48×48 屏幕空间网格遮挡区间裁剪和邻接线条合并。交线候选由 AABB BVH 筛选。
- 可见线条生成带 previous/next 与笔画 UV 的三角带；Shader 每帧按当前相机展开像素宽度。纹理按整条笔画映射，兼容随机长度和粗细曲线。
- Update Rate 限制快照/更新频率；同一相机最多一个后台任务。镜头、姿态和输入不变时复用结果。Scene/Game 相机分别缓存。
- 动画蒙皮快照与 Mesh 上传仍在主线程，高面数角色建议降低 Update Rate；需要时关闭 Intersections、限制 Occluder Layers。任务延迟会造成运动中线稿短暂滞后。
- CPU 遮挡使用可读的 MeshRenderer/SkinnedMeshRenderer 不透明三角形；不把 Alpha Cutout 纹理孔洞、Terrain、粒子当作精确几何遮挡。
- 与网页一致：可见性在偏移和艺术化增长之前确定。延伸线和偏移线不再次做遮挡裁剪。
- 非 XR 单视图实现，未做立体渲染/WebGL 线程平台适配。运行时直接修改 Mesh 拓扑需重新创建 Renderer Feature（常规骨骼/BlendShape 动画不需要）。

## 验证

`SpiderVerse > Line Art > Run Geometry Checks` 检查随机长度上下界、对称切线延伸、曲线 UV、遮挡区间、近裁剪、焊接/合并和闭环纹理接缝。


## 编辑模式预览

不需要进入 Play。后台任务完成、参数调整和相机变化后会按需刷新；静止时不会持续请求重绘。

- **Game 视图**：相机的 Rendering → Renderer 选择 LineArt_Renderer，保持 Source 组件启用。
- **Scene 视图**：URP 17 固定使用当前 URP Asset 的默认 Renderer，不跟随 Main Camera 的 Renderer 选择。要看到线稿，在当前质量等级对应的 URP Asset（PC_RPAsset 或 Mobile_RPAsset）Renderer List 中将 LineArt_Renderer 设为 Default，并勾选 Feature 的 Show In Scene View。这也会影响选择 Use Pipeline Settings 的其他相机；需要原效果的相机请显式选择原 Renderer。
- 编辑模式不会自动播放 Animator；可以通过 Animation 窗口预览姿态。

## GPU Geometry 路径

Renderer Feature 的 Execution Mode 可在 `CpuReference` 与 `GpuGeometry` 之间切换。GPU 路径需要 Geometry Compute 引用 `ObjectLineArt.compute`、GPU Stroke Shader 引用 `ObjectLineArtGpu.shader`；保留原 Stroke Shader 供 CPU 对照与回退使用。

- 初始化缓存焊接邻接、原始顶点索引和空间 BVH。只有 Source 指定对象建立候选边；其他环境网格只参与遮挡/交线。蒙皮对象仅初始化时 BakeMesh 一次，用实际姿态建立 BVH。
- 后续直接读取 Unity 的 GPU 蒙皮顶点缓冲，GPU 完成轮廓筛选、BVH 更新、交线、几何遮挡裁剪、可见边连接、整笔长度与 UV、条带生成及间接绘制。没有逐帧 CPU 顶点回读、BakeMesh 或描边 Mesh 上传，也不增加场景深度预渲染。
- 姿态不变时复用交线；相机和输入均不变时复用整个结果。沿用 Update Rate，重画缓存结果的帧不扫描/排序整个场景。每秒只异步回读少量计数供诊断。
- 粗细、收尖、纹理、噪声、偏移、随机长度继续适用。按连接后的可见链生成笔划，不是每条三角形边各自一笔。相机改变轮廓归属时，完整笔划身份仍可能改变；CPU/GPU 的随机种子与数值阈值并非逐像素一致。
- 当前主要验证环境为 Unity 6000.0.48f1 / URP 17 / Windows D3D11 / RTX 4060 Laptop。要求 Compute Shader、可读网格拓扑和可访问的 GPU 蒙皮缓冲。静态批处理输入不受支持，会回退 CPU；容量/链长度溢出也会报告并回退。
- 原地修改网格拓扑后调用 Feature 的 `InvalidateGpuGeometry()`。不覆盖 Alpha Cutout 孔洞、Terrain、粒子或 XR；沿用原几何遮挡的范围。

GPU 自动检查入口为 `SpiderVerse.LineArt.Editor.LineArtGpuChecks.Run`，只允许在 `Library/LineArtValidation` 隔离工程中以 batch 模式运行，避免改动正在编辑的场景。检查闭合轮廓、局部遮挡、交线及交线缓存，并与 CPU 对照；完整场景还核对 GPU 蒙皮位置。

独立程序加 `-lineArtBenchmark -lineArtMode cpu` 或 `gpu` 可运行对照测试。基准预热 8 秒、采样 12 秒，显式向 960×960 RenderTexture 渲染完整场景，避免隐藏窗口跳过渲染。结果为离屏场景吞吐，不等于编辑器窗口 FPS。`-lineArtMotion moving` 移动相机并保留角色动画；`-lineArtOutput <绝对路径前缀>` 输出统计与截图。GPU 硬件时间不可用时计数为 0，不应将其解释为零 GPU 开销。可选 `-lineArtProfile` 启用阶段 Recorder，正常基准默认不开启。

### 2026-09-22 性能验收

RTX 4060 Laptop，D3D11，Development Player，960×960 全场景离屏渲染，动画开启、相机移动，Update Rate 保持 30；遮挡、交线、粗细曲线和长度随机均保持开启。最终版本交替运行 CPU/GPU 各两轮，每轮预热 8 秒后采样 12 秒：

| 轮次 | CPU Reference 平均 FPS | GPU Geometry 平均 FPS | 提升 | CPU / GPU P95 帧耗时 |
| --- | ---: | ---: | ---: | ---: |
| 1 | 723.053 | 768.832 | 6.3% | 1.900 / 1.773 ms |
| 2 | 683.882 | 708.536 | 3.6% | 1.935 / 2.090 ms |

两轮平均约提升 5%。这是有限采样的离屏吞吐提升，不保证编辑器窗口获得同等 FPS；第二轮 P95 变差，尚不能宣称尾部卡顿稳定改善。GPU 时间戳在该离屏测试中不可用，因此没有把计数为零的硬件计时当作性能证据。早期隐藏窗口未渲染产生的上万 FPS 数据全部作废。

原始统计与画面：`Library/LineArtValidation/Validation/final2-{cpu,gpu}-*.txt/.png`；最终几何验证/构建日志：`Library/LineArtValidation/gpu-final2-check.log`。完整场景包含 1340 个网格、46944 个三角形；GPU 候选结构只为描边目标保留 24510 条边。通过三个几何夹具、交线缓存复用、14 个蒙皮对象位置比较（最大世界位置误差小于 0.000008）；无容量溢出或回退记录。

本轮保留原 CPU 算法作为参考。整场景的候选/可见片段数量与 CPU 并非完全一致，且随机笔划身份使用新的稳定键；保留长笔划连接不等于承诺逐像素复现。GPU 不支持的输入会在 Console 说明并回退 CPU。

实现依据：[NVIDIA 邻接面轮廓判断](https://developer.nvidia.com/gpugems/gpugems3/part-ii-light-and-shadows/chapter-11-efficient-and-robust-shadow-volumes-using)、[Unity 6 GPU 蒙皮顶点缓冲 API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer.GetVertexBuffer.html)。
