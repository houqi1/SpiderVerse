# Motion Vector 后处理

适用于本项目的 Unity 6000.0 / URP 17.0.4。使用自定义 `MotionVectorNoiseFeature`，兼容 Render Graph 与 Compatibility Mode。噪声旋转与所有调试模式共用同一张停帧运动矢量缓存；原生运动矢量保持逐帧生成。

## 使用

1. 等待 Unity 导入脚本和 Shader，点击 `SpiderVerse > Setup Motion Vector Debug`。
2. 菜单会在当前 URP 的默认 Renderer 上添加并启用 `Motion Vector Debug`。如果在 Project 中先选中某个 Universal Renderer Data，则安装到所选 Renderer；相机指定了 Renderer Override 时，应选中对应资源。
3. 进入 Play Mode，在 Game 视图移动相机或物体。
4. 在 `Assets/Shader/MotionVectorDebug.mat` 中调整 Display Mode。默认 NoiseOverlay，将噪声叠加到场景；Direction / SignedRG / Speed 使用 Sensitivity 放大运动。关闭 Renderer 上的 `Motion Vector Debug` Feature 即恢复正常渲染。重复执行菜单不会重复添加。

当前 `LineArt_Renderer` 已迁移到自定义 Feature。其他 Renderer 可以执行同一安装菜单，旧版 Full Screen Pass 会被禁用，避免重复叠加。也可以手动添加 Motion Vector Noise Feature 并指定上述材质；它会请求 Motion 输入并在 After Rendering Post Processing 执行。相机堆栈只在 Base Camera 上应用，Overlay Camera 继续在其上绘制。

## 更新频率与方向保持

频率设置位于 Renderer 的 **Motion Vector Debug** Feature 上，纹理、颜色和输出模式仍在材质上。

| Feature 参数 | 含义 |
| --- | --- |
| Update Mode = Sync With Animation | 可选。跟随相机当前激活的 Line Art Feature 的采样 generation，在该姿势的运动矢量生成后发布方向缓存。调整 Line Art 的 Update Rate 即可同步调整动画、线条与噪声方向节奏。 |
| Update Mode = Fixed Rate | 默认。按时间以 Update Rate 指定的 Hz 更新运动矢量缓存；两次更新之间保持整张纹理不变，无插值。 |
| Update Rate | 默认 12 Hz，范围 1–60。同步模式下仅在找不到激活的 Line Art Feature 时作为固定频率回退值；实际可见更新次数不超过渲染帧率。 |
| Hold Last Direction | 默认关闭：每次刷新用当前整张运动矢量覆盖缓存，无有效运动的像素归零。开启后会逐像素保留最后一次运动矢量和运动遮罩，可能在物体经过的旧屏幕位置留下旋转残影。它与刷新间隔内的整张纹理停帧是两个独立行为。 |
| Reset After Seconds | 默认 0，表示无限保持。设置 0–30 秒的延迟后，无有效运动的像素恢复默认方向；改变在下次发布时可见。计时精度约 1/32 秒，使用绝对时间戳避免高帧率下半精度计数器停滞。 |
| Show In Scene View | 是否同时在 Scene 视图应用。Game 和 Scene 相机拥有独立缓存。 |
| Camera Cut Distance / Angle | 相机单次渲染间的位置/旋转变化超过阈值时重置缓存；距离单位为世界单位，距离 0 表示关闭位置判定。 |

默认组合是 Fixed Rate + Hold Last Direction 关闭，无插值。Reset After Seconds 仅在开启 Hold Last Direction 时有效。Feature 的右键菜单 **Reset Direction History** 可手动清空；相机分辨率变化、明显投影变化、暂停渲染后重新出现及镜头跳切也会重建有效历史。

采集与发布分开：GPU 每帧将当前有效运动矢量（RG 保存原始有符号 UV 位移，保留方向和大小）写入候选缓存，零运动按保持/超时设置处理；仅在固定时钟或动画采样变化时，将候选缓存复制到噪声旋转和调试模式共同读取的发布纹理。这样固定时钟错过动画运动脉冲时，仍能读取之前捕获的有效方向。低于阈值的运动不算有效；关闭 Hold Last Direction 后，固定时钟仍可能看到零运动帧。

Direction / SignedRG / Speed 与 NoiseOverlay / NoiseOnly / Output Mapped Noise Only 全部读取同一张发布缓存。可将 Update Rate 设为 1 Hz，观察调试画面每秒更新一次，期间旋转噪声所用的运动矢量纹理完全保持。首次渲染、尺寸变化和历史重置会立即刷新。底层场景仍正常渲染，所以叠加模式下场景继续运动；直接输出模式更容易观察噪声本身的停帧。

每个相机使用三张 RGBA16F 纹理（候选双缓冲 + 发布缓存），1080p 约 47.5 MiB，另有每帧采集和定时复制开销。降低这里的 Hz 调整的是视觉节奏，不代表原生运动矢量生成成本降低。固定模式使用不受 Time Scale 影响的时钟，播放暂停时不会自行推进；同步模式跟随已有 Line Art 时钟。

## 输出含义

| Display Mode | 显示 |
| --- | --- |
| Direction | 色相表示缓存运动方向，亮度表示缓存位移大小；缓存为零时为黑色。 |
| SignedRG | R 表示水平分量，G 表示垂直分量；`RG = saturate(0.5 + motion * Sensitivity)`，B 固定为 0.5。零运动输出中性灰。 |
| Speed | `saturate(length(motion * Sensitivity))` 灰度图；缓存为零时为黑色。 |
| NoiseOverlay | 将逐像素按运动方向旋转的噪声透明叠加到场景。 |
| NoiseOnly | 单独显示旋转后的噪声灰度，便于观察方向。 |

## 屏幕空间噪声旋转

每个像素先采样停帧缓存中自身的运动矢量，再将其转换成像素单位并归一化，作为噪声的 X 轴方向。噪声坐标围绕屏幕中心做逆向采样旋转，已考虑屏幕宽高比，因此横屏下的斜向运动不会被拉偏。不同像素可以采用不同角度，并不是将整张屏幕统一旋转。

默认使用已有的 `CharacterParticleNoise.png` 的 R 通道，Tiling = (4, 24)，让噪声沿 X 轴呈拉长形态以便看清方向。可直接替换材质的 Noise Map；采样使用 Repeat，不改变原贴图的导入设置。

| 材质参数 | 含义 |
| --- | --- |
| Output Mapped Noise Only | 在 NoiseOverlay 模式开启时直接输出映射后的噪声，保留 Contrast、Tint RGB 和运动遮罩，忽略 Opacity 与 Tint Alpha，完全覆盖场景颜色。默认关闭；NoiseOnly 模式本身已经直接输出灰度。 |
| Noise Map 的 Tiling / Offset | 噪声缩放和偏移；Tiling 按屏幕高度为单位，X 小、Y 大时噪声沿运动方向拉长。 |
| Noise Opacity | 叠加透明度，0 为原画面，1 为完全显示噪声。 |
| Noise Tint | 噪声染色，Alpha 进一步控制叠加强度。 |
| Noise Contrast | 噪声灰度围绕 0.5 调整对比度。 |
| Noise Angle Offset | 在运动方向上增加角度偏移；原贴图纹理沿 Y 轴时可尝试 90 度。 |
| Direction Threshold | 单位是像素/帧；低于此速度使用默认朝向，避免近零向量造成随机方向。 |
| Noise Only On Moving Pixels | 开启后使用缓存的运动遮罩；Hold Last Direction 开启时，最后一次有效运动的遮罩也保持，直到超时或重置。 |

NoiseOverlay 通过硬件 Alpha Blend 叠加，不需要复制场景颜色。Sensitivity 仅影响运动矢量调试模式，不影响噪声方向或阈值。NoiseOnly 忽略 Tint 和 Opacity，以显示灰度纹理。

首次使用或历史重置时，无运动像素采用 Angle Offset 指定的默认方向。历史保存在屏幕空间，没有重投影或遮挡检测；持续移动的相机/物体可能让旧方向暂时落在新的表面或背景上，方向突变处也可能出现不连续。无限保持时这种旧方向可以一直保留，可以缩短 Reset After Seconds 或手动重置。这里旋转的是纹理采样坐标，不会随运动平流噪声。

采样 `_MotionVectorTexture.rg`，原始数据为有符号的 `currentUV - previousUV`，单位是每帧 UV 位移。Sensitivity 默认 100，用于放大小运动；这些显示模式都是可视化，不是无损原始数据导出。最终显示仍可能经过输出颜色空间转换、抗锯齿或放大处理，因此不要从屏幕颜色反推精确数据。

## 物体运动矢量

Requirements = Motion 会要求 URP 生成纹理，不需要为了查看它额外开启 TAA 或 Motion Blur。摄像机运动和物体运动均来自 URP 原有的 motion vector pass。

物体需要 Renderer 的 Motion Vectors 设为 Per Object Motion，并且材质 Shader 实现 `LightMode = MotionVectors` 的 Pass。蒙皮角色还需要开启 Skinned Motion Vectors，供 Unity 保留上一帧的蒙皮位置。

本项目 Gwen 的材质使用 `Custom/Toon`，已为该 Shader 添加 URP 标准 `MotionVectors` Pass：通过 `TEXCOORD4` 读取上一帧蒙皮位置，结合前一帧物体/相机矩阵计算运动；即使根节点不移动，骨骼动画也能贡献运动矢量。Pass 使用与正常渲染相同的 `_Cull` 和 `ToonInput.hlsl` 材质布局。当前 Toon 不做顶点位移或 Alpha Clip；以后添加这些功能时，也要同步更新运动矢量 Pass。

在 Hierarchy 选中角色根节点，执行 `SpiderVerse > Enable Selected Character Motion Vectors`，可一次开启子节点中所有 SkinnedMeshRenderer 的 Per Object Motion 和 Skinned Motion Vectors，并记录可撤销的场景/Prefab 实例修改。需要保存场景才能保留这些设置。菜单在编辑模式下可用。

`LineArtPreview` 的角色使用 `LineArtSteppedAnimator` 抽帧动画。保持姿势的渲染帧可能没有物体运动矢量；姿势跳到下一采样帧时才有明显输出。验证时固定相机并持续播放，或临时禁用该抽帧组件，对比连续 Animator 播放。

URP 17 的常规物体运动矢量面向不透明/Alpha Clip 材质；透明物体、描边或 GPU 粒子等额外绘制不一定贡献运动矢量。检查角色自身运动时，先固定相机，并使用支持 motion vectors 的 URP Lit 材质作对照。

## 验证记录

此前版本在独立的 Unity 6000.0.48f1 / D3D11 验证工程中验证了：30/60/144/240 FPS 下的 6/12/24 Hz 调度、同帧重复请求、卡顿后的跳过过期时刻、动画 generation 同步及失去同步后的回退；Shader 两个 Pass 编译及 GPU 上的方向保持、超时、时间戳绕回、负方向存储、直接输出与透明叠加；Render Graph / Compatibility Mode 下的双相机独立缓存、目标尺寸变化和镜头跳切。实际角色场景的视觉风格仍应按需要调整频率、阈值和噪声参数。

本次统一缓存数据源、保留原始 RG 位移并切换默认 Fixed Rate 的修改尚未运行 Unity 验证。

修正了候选双缓冲交换后按读写位置重命名的问题：URP 的重新分配判断包含纹理名称，原实现因此每帧重新分配并重置发布时钟。现在名称随纹理保留，正常帧之间交换读写角色不会触发该重置。本次修正尚未运行 Unity 验证。

逐次旋转残留：旧默认配置 Hold Last Direction 开启且 Reset After Seconds = 0，会无限保留物体经过位置的运动方向。现已关闭 Feature 的默认值及 LineArt_Renderer 中的该选项；停帧依然由发布时钟控制，每次发布完整替换旧缓存。此更改尚未在 Unity 中运行验证。
