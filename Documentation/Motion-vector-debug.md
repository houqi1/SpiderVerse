# Motion Vector 后处理

适用于本项目的 Unity 6000.0 / URP 17.0.4。使用自定义 `MotionVectorNoiseFeature`，兼容 Render Graph 与 Compatibility Mode。噪声旋转与所有调试模式共用同一张停帧运动矢量缓存；原生运动矢量保持逐帧生成。

## 使用

1. 等待 Unity 导入脚本和 Shader，点击 `SpiderVerse > Setup Motion Vector Debug`。
2. 菜单会在当前 URP 的默认 Renderer 上添加并启用 `Motion Vector Debug`。如果在 Project 中先选中某个 Universal Renderer Data，则安装到所选 Renderer；相机指定了 Renderer Override 时，应选中对应资源。
3. 进入 Play Mode，在 Game 视图移动相机或物体。
4. 在 `Assets/Shader/MotionVectorDebug.mat` 中调整 Display Mode。默认 NoiseOverlay，将噪声叠加到场景；Direction / SignedRG / Speed 使用 Sensitivity 放大运动。关闭 Renderer 上的 `Motion Vector Debug` Feature 即恢复正常渲染。重复执行菜单不会重复添加。

当前 `LineArt_Renderer` 已迁移到自定义 Feature。其他 Renderer 可以执行同一安装菜单，旧版 Full Screen Pass 会被禁用，避免重复叠加。也可以手动添加 Motion Vector Noise Feature 并指定上述材质；它会请求 Motion 输入并在 Before Rendering Post Processing 执行。场景扭曲和加色叠加完成后，再进入 URP 后处理，因此结果会受到 Bloom、色调映射等已启用效果的影响。相机堆栈只在 Base Camera 上应用，Overlay Camera 继续在其上绘制。

## 更新频率与方向保持

频率设置位于 Renderer 的 **Motion Vector Debug** Feature 上，纹理、颜色和输出模式仍在材质上。

| Feature 参数 | 含义 |
| --- | --- |
| Update Mode = Sync With Animation | 可选。播放时优先跟随 LineArtSteppedAnimator 完成姿势更新后发出的采样序号，保存当前整张 MV，并发布上一次成功捕获的动画采样 MV。没有对应抽帧组件时才跟随 Line Art 的相机采样时钟。动画保持姿势的中间渲染帧不覆盖候选缓存；迟到的重绘保留已有结果。首次成功捕获姿势更新时直接显示该次结果。切换同步来源、模式或进入回退时重建历史。 |
| Update Mode = Fixed Rate | 默认。按时间以 Update Rate 指定的 Hz 更新运动矢量缓存；两次更新之间保持整张纹理不变，无插值。 |
| Update Rate | 默认 12 Hz，范围 1–60。同步模式下仅在找不到激活的 Line Art Feature 时作为固定频率回退值；实际可见更新次数不超过渲染帧率。 |
| Hold Last Direction | 默认关闭：每次刷新用当前整张运动矢量覆盖缓存，无有效运动的像素归零。开启后会逐像素保留最后一次运动矢量和运动遮罩，可能在物体经过的旧屏幕位置留下旋转残影。它与刷新间隔内的整张纹理停帧是两个独立行为。 |
| Reset After Seconds | 默认 0，表示无限保持。设置 0–30 秒的延迟后，无有效运动的像素恢复默认方向；改变在下次发布时可见。计时精度约 1/32 秒，使用绝对时间戳避免高帧率下半精度计数器停滞。 |
| Show In Scene View | 是否同时在 Scene 视图应用。Game 和 Scene 使用各自视角的独立 MV 缓存；播放时 Scene 优先跟随以 Main Camera 为参考的抽帧角色，没有匹配时跟随第一个活动抽帧角色。 |
| Camera Cut Distance / Angle | 相机单次渲染间的位置/旋转变化超过阈值时重置缓存；距离单位为世界单位，距离 0 表示关闭位置判定。 |

默认组合是 Fixed Rate + Hold Last Direction 关闭，无插值。Reset After Seconds 仅在开启 Hold Last Direction 时有效。Feature 的右键菜单 **Reset Direction History** 可手动清空；相机分辨率变化、明显投影变化、镜头跳切、材质或同步来源变化也会重建历史。Game 相机暂停渲染后重新出现会重建历史；Scene 普通漏帧或暂时隐藏不清空缓存。Inspector 验证触发的 Create 不再无条件清空历史。

采集与发布分开：候选纹理 RG 保存原始有符号 UV 位移，保留方向和大小；零运动按保持/超时设置处理。Sync With Animation 仅在动画采样变化时采集 MV，并发布上一次动画采样的候选纹理。例如第 N 次采样写入 MV(N)，同时显示 MV(N−1)，中间保持姿势的渲染帧不读写候选历史。这样普通静止渲染帧的零 MV 不会覆盖上一次动画采样。Fixed Rate（包括同步不可用时的回退）仍每渲染帧采集，并在时钟到期时发布当前候选纹理。两种模式的 Mask、旋转方向与调试视图均共用发布纹理，两次发布之间整张纹理不变。开启 Hold Last Direction 时，候选纹理仍包含逐像素保留的方向；默认关闭时如果动画采样帧本身为零运动，仍会保存并显示该零值。缓存保留非零运动；Direction Threshold 仅控制接近零时的旋转朝向。

Direction / SignedRG / Speed 与 NoiseOverlay / NoiseOnly / Output Mapped Noise Only 全部读取同一张发布缓存。可将 Update Rate 设为 1 Hz，观察调试画面每秒更新一次，期间旋转噪声所用的运动矢量纹理完全保持。首次渲染、尺寸变化和历史重置会立即刷新。底层场景仍正常渲染，所以叠加模式下场景继续运动；直接输出模式更容易观察噪声本身的停帧。

每个相机使用三张 RGBA16F 纹理（候选双缓冲 + 发布缓存），1080p 约 47.5 MiB，另有采集和定时复制开销。降低这里的 Hz 调整的是视觉节奏，不代表原生运动矢量生成成本降低。固定模式使用不受 Time Scale 影响的时钟，播放暂停时不会自行推进；同步模式跟随已有 Line Art 时钟。

### Scene 视图的同步

LineArtSteppedAnimator 在完成 Animator.Update 后记录实际采样序号及 Time.frameCount，并请求 Scene 重绘（同一帧多个角色只请求一次）。后处理读取这个信号，不为 Scene 单独推进动画时钟。Game 选择以自身相机为参考的抽帧组件；Scene 优先选择以 Main Camera 为参考的组件，否则选择第一个活动组件。

如果 Scene 重绘晚于该次姿势更新帧，就保留候选和发布纹理，等待下一次同帧捕获的动画采样，避免将静止帧的零 MV 当成新的动画样本。Mask 与旋转方向始终来自同一张发布缓存。真正重置后先初始化纹理；第一张成功捕获的动画样本会直接显示，不把初始化的空缓存当作上一动画样本发布。Scene 的 MV 仍按自身视角生成，不复用 Game 的屏幕空间纹理。成功采到的姿势更新帧如果确实没有运动，仍允许缓存归零。

## 输出含义

材质的 **Motion Vector Source Debug** 有两个开关：**Output Third Layer MV (Current Animation Sample)** 显示第三层使用的 MV；**Output Other Layers MV (Published Sample)** 显示前两层使用的发布快照。Sync With Animation 下，第三层通常为当前动画采样 MV(N)，前两层为 MV(N−1)；Fixed Rate 下两者都显示发布快照。两个开关同时开启时，第三层开关优先。

开启任一开关后会覆盖 Display Mode 和 Output Mapped Noise Only，以 Signed RG 显示对应的整张 MV：R=X、G=Y，零矢量为中性灰，Sensitivity 控制放大倍率。显示原始屏幕布局，不应用第二层的 UV 缩放或噪声/速度遮罩。MV 的采集和发布继续；开启时场景扭曲层不执行，关闭开关后恢复效果。调试图仍可能经过后续 URP 后处理，不是无损数据导出。

| Display Mode | 显示 |
| --- | --- |
| Direction | 色相表示缓存运动方向，亮度表示缓存位移大小；缓存为零时为黑色。 |
| SignedRG | R 表示水平分量，G 表示垂直分量；`RG = saturate(0.5 + motion * Sensitivity)`，B 固定为 0.5。零运动输出中性灰。 |
| Speed | `saturate(length(motion * Sensitivity))` 灰度图；缓存为零时为黑色。 |
| NoiseOverlay | 按缓存运动矢量的像素速度生成平滑遮罩，将旋转纹理的白色亮度乘以遮罩和指定颜色，再加到场景颜色。 |
| NoiseOnly | 单独显示旋转后的噪声灰度，便于观察方向。 |

## 屏幕空间噪声旋转

每个像素先采样停帧缓存中自身的运动矢量，再将其转换成像素单位并归一化，作为噪声的 X 轴方向。噪声坐标围绕屏幕中心做逆向采样旋转，已考虑屏幕宽高比，因此横屏下的斜向运动不会被拉偏。不同像素可以采用不同角度，并不是将整张屏幕统一旋转。

默认使用已有的 `CharacterParticleNoise.png` 的 R 通道，Tiling = (4, 24)，让噪声沿 X 轴呈拉长形态以便看清方向。可直接替换材质的 Noise Map；采样使用 Repeat，不改变原贴图的导入设置。

| 材质参数 | 含义 |
| --- | --- |
| Output Mapped Noise Only | 在 NoiseOverlay 模式开启时直接输出映射后的噪声，保留 Contrast、Tint RGB 和运动遮罩，忽略 Add Intensity 与 Tint Alpha，完全覆盖场景颜色。默认关闭；NoiseOnly 模式本身已经直接输出灰度。 |
| Noise Map 的 Tiling / Offset | 噪声缩放和偏移；Tiling 按屏幕高度为单位，X 小、Y 大时噪声沿运动方向拉长。 |
| Noise Add Intensity | 加色强度，0 不贡献颜色，1 使用完整颜色强度；内部沿用 _NoiseOpacity 属性。 |
| Noise Tint | 白色区域所添加的颜色，支持 HDR；Alpha 进一步控制加色强度。 |
| Noise Contrast | 噪声灰度围绕 0.5 调整对比度。 |
| Noise Angle Offset | 在运动方向上增加角度偏移；原贴图纹理沿 Y 轴时可尝试 90 度。 |
| Direction Threshold | 单位是像素/帧；低于此速度使用默认朝向，避免近零向量造成随机方向。 |
运动遮罩始终开启：`speed = length(缓存 MV × 屏幕尺寸)`，`mask = smoothstep(Mask Start Speed, Mask Full Speed, speed)`。URP 原始 MV 的静止值为 (0,0)，不减去 0.5；向左或向下的负分量运动同样参与长度计算。

材质 **Motion Mask** 中的 **Mask Start Speed** 默认为 0.1 像素/帧，低于该值不生效；**Mask Full Speed** 默认为 2 像素/帧，达到该值完全生效，中间平滑过渡。相等或反向设置时，上限会在 Shader 中限制为至少比下限大 0.001，避免无效插值。这里使用采样帧的位移大小，不是按秒计算的速度；抽帧和连续动画可能需要不同参数。

加色、NoiseOnly、Output Mapped Noise Only 和前两层场景扭曲共用这一速度遮罩；Direction / SignedRG / Speed 调试输出仍显示缓存原始数据。加色层的黑色贡献为零、灰色按亮度贡献；第一层按 Distortion Map Cutoff 重映射为非负强度，第二层直接使用非负噪声值。遮罩筛选屏幕运动，也会包含相机移动引起的背景运动，不等于角色身份遮罩。

NoiseOverlay 通过硬件加法混合实现：`结果 RGB = 场景 RGB + 运动遮罩 × 旋转后纹理亮度 × Noise Tint.rgb × saturate(Add Intensity × Tint.a)`，保留场景 Alpha。开启下方的场景扭曲层时，先对本帧场景颜色副本进行偏移，再加上噪声颜色；关闭扭曲时，加色本身不需要复制场景。直接输出和调试模式仍替换场景 RGB。Sensitivity 仅影响运动矢量调试模式，不影响噪声方向或阈值。NoiseOnly 忽略 Tint 和 Add Intensity，以显示灰度纹理。

## Motion Vector 场景扭曲层

在材质的 **Motion Oriented Scene Distortion** 中设置：

| 参数 | 含义 |
| --- | --- |
| Enable Scene Distortion | 启用第一层。仅 NoiseOverlay 且 Output Mapped Noise Only、MV 调试开关关闭时执行。 |
| Distortion Strength Map (R) | 独立的强度贴图，按缓存 MV 方向旋转后采样 R 通道。原始采样值通过 R × 2 − 1 映射为有符号强度。 |
| Tiling / Offset | 强度贴图独立的缩放和偏移，屏幕空间、Repeat 采样。 |
| Distortion Distance (Pixels) | 两侧最大偏移距离，默认 8 像素。负值翻转两侧方向，0 关闭本层。 |
| Distortion Map Angle Offset (Degrees) | 额外旋转强度贴图，不改变场景偏移方向。 |

`strength = saturate((noise.R - Distortion Map Cutoff) / (1 - Distortion Map Cutoff))`

`偏移像素 = normalize(缓存 MV × 屏幕尺寸) × strength × Distortion Distance × 速度遮罩`

`输出 RGB = SceneColor(UV − 偏移像素 / 屏幕尺寸) + 原有加色层`

Distance 为正时，只有高于 Cutoff 的噪声区域产生偏移，强度从 Cutoff 到 1 线性重映射到 0 到 1；低于 Cutoff 时不偏移。加色层仍使用原有的非负噪声亮度。

低于 Mask Start Speed 的像素不偏移。方向、图案旋转与速度遮罩均使用发布的 MV 快照，遵循 Fixed Rate / Sync With Animation。SceneColor 为本帧该效果执行前的相机颜色，每帧复制，不累积历史颜色。屏幕边缘限制到有效采样区域。

## Motion Vector 偏移拖影层

第二层在第一层之后执行，用缩放后的屏幕位置采样 Motion Vector、SceneColor 和自己的噪声图。场景采样 UV 围绕屏幕中心缩放，UV Scale=(1,1) 时不缩放。

| 材质参数 | 含义 |
| --- | --- |
| Enable Offset Smear | 启用第二层。 |
| Offset Smear Map (R) | 独立噪声图，R 值直接作为非负偏移强度和混合权重。 |
| Offset Smear UV Scale | SceneColor 与偏移噪声的屏幕 UV X/Y 缩放；MV 在同一缩放位置采样。 |
| Offset Smear Distance (Pixels) | 两侧最大运动偏移像素数，独立于第一层；负值翻转方向。 |
| Offset Smear Map Angle Offset (Degrees) | 噪声坐标额外旋转角度，不改变运动方向。 |

`offsetNoise = saturate(OffsetSmearMap.R)`

`offsetUV = 0.5 + (screenUV − 0.5) × UVScale − normalize(缓存 MV × 屏幕尺寸) × offsetNoise × OffsetSmearDistance × 速度遮罩 / 屏幕尺寸`

`对应的未缩放 UV = 0.5 + (offsetUV − 0.5) / UVScale`

`originalDistortionPixels = DistortionDistance × (对应位置的 DistortionMap.R * 2 - 1) × 对应位置的速度遮罩`

`allowOffsetSmear = (对应位置的 DistortionMap.R <= Distortion Map Cutoff) ? 1 : 0`

`blend = saturate(offsetNoise × 速度遮罩 × allowOffsetSmear)`

`输出 RGB = lerp(SceneColor(screenUV), SceneColor(offsetUV), blend)`

第一层遮罩仍在反向映射回去的未缩放屏幕位置查询，并使用该位置的 MV 方向和速度。只要该位置的第一层噪声高于 Cutoff，就阻止第二层叠加；低于或等于 Cutoff 时允许叠加。第一层关闭、距离为零或速度遮罩为零时，第二层也允许叠加。第一层贴图若大部分区域高于 Cutoff 且存在运动，第二层可叠加区域就会较少。

第二层使用非负 noise 控制沿 MV 方向的偏移，Lerp 权重也在 [0,1]；noise=0 时不做运动偏移，也不混入缩放后的颜色。场景采样与反向映射坐标均限制在屏幕内，缩放分量接近零时有除零保护。两个层均开启时，第二层处理第一层的输出，增加一次颜色复制和一次全屏绘制。

## Motion Vector 未缩放场景扭曲层

第三层在前两层之后执行，直接在未缩放的屏幕 UV 上采样 SceneColor，并按独立的强度贴图扭曲。它使用当前动画采样的 Motion Vector，而前两层继续使用发布的历史采样；Sync With Animation 下当前采样 MV(N) 会在动画采样之间保持，Fixed Rate 下则使用定时发布的快照。该层的速度遮罩、扭曲距离、噪声图和角度均可独立调整。

| 材质参数 | 含义 |
| --- | --- |
| Enable Unscaled Scene Distortion | 启用第三层。仅 NoiseOverlay 且 Output Mapped Noise Only、MV 调试开关关闭时执行。 |
| Unscaled Distortion Strength Map (R) | 独立的强度贴图，按当前 MV 方向旋转后采样 R 通道。 |
| Tiling / Offset | 第三层强度贴图独立的缩放和偏移，屏幕空间、Repeat 采样。 |
| Unscaled Distortion Map Cutoff | 强度阈值；低于阈值的区域不偏移，高于阈值后线性映射到完整强度。 |
| Unscaled Distortion Distance (Pixels) | 最大偏移距离，默认 8 像素。负值翻转方向，0 关闭本层。 |
| Unscaled Distortion Map Angle Offset (Degrees) | 额外旋转强度贴图，不改变场景偏移方向。 |
| Unscaled Mask Start / Full Speed | 独立速度遮罩范围，单位为像素/采样帧。 |

`strength = saturate((noise.R - cutoff) / (1 - cutoff))`

`偏移像素 = normalize(当前动画采样 MV × 屏幕尺寸) × strength × Unscaled Distortion Distance × 独立速度遮罩`

`输出 RGB = SceneColor(UV − 偏移像素 / 屏幕尺寸)`

Sync With Animation 下，第三层使用当前姿势更新的 MV(N)，该纹理在后续保持姿势的渲染帧间保持不变；前两层仍使用发布的 MV(N−1)。没有新的动画采样时，第三层继续沿用最近一次采到的当前 MV，而不是每个渲染帧重新读取 Motion Vector。

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

本次白色区域加色修改尚未运行 Unity 验证。
