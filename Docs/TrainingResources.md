# [FRAME] 训练小游戏：场景、人物、器械和动作资源清单

[FRAME｜已确认] 2026-10-22 用户确认资源调研与工程演示采用以下 8 个候选动作：卧推、哑铃弯举、高位下拉、绳索下压、深蹲、臀推、肩推、侧平举。正式小游戏操作、评分、数值与最终动作名单仍需分别确认。

[INFERRED｜HIGH] 原资源检索时间为 2026-10-21 至 2026-10-22；报价为网页当时展示的美元价格，不包含税费。

## [FRAME] 美术方向与筛选准则

[FRAME｜已确认] 美术方向：风格化卡通 3D。既有分段几何人物仅用于资产和构建验证，不能作为该风格已经制作完成的依据。

[FRAME｜制作建议] 人物采用统一骨架、清晰轮廓和适度简化的体块；器械统一造型语言与材质。卡通化优先作用于外观，训练动画仍需保持可辨认的关节运动、握持与器械接触。具体头身比例、色板、服装和是否描边尚未定稿。

[FRAME｜制作建议] C1 可作为统一卡通角色的候选基底，需补运动服装与统一材质；E1/E2/E3 按器械结构和可拆分性筛选，导入后统一视觉表现。现有 A 类资源优先用作动作与重定向参考；A5 的解剖人体外观不作为最终角色风格。低多边形只作为可选的模型制作方式，不替代风格化卡通的视觉要求。

[FRAME｜待制作] 8 个动作均需匹配风格统一的人物、器械和场景。优先补齐绳索下压的绳把、双手握持、绳索及配重同步；当前直杆原型保留其实际状态，不标为已完成绳把版本。

[FRAME] 推荐路线：先用已制作的原型跑通小游戏，再统一人物骨架和器械比例；采购动画优先检查动作匹配，最后制作教学细节与游戏反馈。以下资源均为参考清单，未购买或导入第三方商店文件。

## [FRAME] 每个候选动作的对应资源

| 候选动作 / 工程 ID | 场景及器械 | 人物与动作来源 | 已核查匹配与剩余工作 |
|---|---|---|---|
| [FRAME] 卧推 `bench-press` | [FRAME] 平凳、支架、杠铃、片；E1/E2 场景候选 | [FRAME] C1 人物；A1 杠铃动画 | [INFERRED｜HIGH] A1 明列 Bench Press；A3 免费演示有宽握卧推。要统一凳高、握距、杠铃接触和起落架动画。当前原型已生成。 |
| [FRAME] 哑铃弯举 `dumbbell-curl` | [FRAME] 哑铃区、成对哑铃；E1/E2 | [FRAME] C1 + A2 | [INFERRED｜HIGH] A2 包含不同弯举变式；先确定掌心方向、单双臂同步，再选剪辑。当前原型为双臂同步，正式变式待定。 |
| [FRAME] 高位下拉 `lat-pulldown` | [FRAME] 滑轮塔、坐垫、拉杆、绳索、配重；E1 的具体器械需清点，或自制 | [FRAME] A5 已包含人物、器械及动作；统一角色可改用 C1 | [INFERRED｜HIGH] A5 直接匹配下拉，但页面标注约 20 万多边形，适合动作参考/重定向研究；不能直接视为移动端预算合格。当前原型已生成。 |
| [FRAME] 绳索三头下压 `triceps-pushdown` | [FRAME] 滑轮塔、绳把、动态绳索、配重；自制或 E1 补件 | [FRAME] C1 + A4 | [INFERRED｜HIGH] A4 明列双手下压剪辑，但参考道具是 cable bar，未证明为绳把变式。需要调整握持、末端分绳和绳索。当前原型是直杆下压，未冒充已制作绳把动作。 |
| [FRAME] 杠铃深蹲 `back-squat` | [FRAME] 深蹲架、杠铃、片；E1/E2 | [FRAME] C1 + A1；A3 可先试免费动作 | [INFERRED｜HIGH] A1/A3 明列 Back Squat；还需起架、回架、失败阶段以及安全杆接触。当前原型已生成。 |
| [FRAME] 臀推 `hip-thrust` | [FRAME] 平凳、杠铃、护垫；E1/E2 补件 | [FRAME] C1 + A1 | [INFERRED｜MED] A1 的 Glute Bridge Bench 是近似候选，需看预览确认是否满足最终臀推变式；不能仅凭名称当作严格匹配。当前原型已生成。 |
| [FRAME] 哑铃肩推 `shoulder-press` | [FRAME] 哑铃区；坐姿版另需靠背凳 | [FRAME] C1 + A2 | [INFERRED｜HIGH] A2 包含 Neutral Shoulder Press、Military Press 等变式；姿势与握法待策划选定。当前原型为站姿双哑铃。 |
| [FRAME] 哑铃侧平举 `lateral-raise` | [FRAME] 哑铃区；E1/E2 | [FRAME] C1 + A2 | [INFERRED｜HIGH] A2 明列前举与侧平举组合，需要切分并修循环，不能把整段直接作为纯侧平举。当前原型已生成独立侧平举循环。 |

## [FRAME] 人物与场景模型来源

| 编号 | 直接来源 | 页面证据 / 费用 / 格式 | 采用判断 |
|---|---|---|---|
| C1 | [INFERRED｜HIGH] [Quaternius Universal Base Characters](https://quaternius.com/packs/universalbasecharacters) · [下载档位](https://quaternius.itch.io/universal-base-characters/purchase) | [INFERRED｜HIGH] 人形基础角色、FBX/glTF；页面标 CC0。Standard 有免费内容；Source 页面为 $19.99，不能将完整 Source 包宣称免费。 | [FRAME｜制作建议] 风格化卡通角色的候选基底；选定模型后做 Humanoid 骨架映射、运动服装、统一材质与肌肉变化设计。 |
| E1 | [INFERRED｜HIGH] [VNB Gym Set](https://vnb3d.com/product/gym-set/) · [发布页](https://vnbp.itch.io/low-poly-gym-set) | [INFERRED｜HIGH] 70+ 低多边形健身模型，免费，FBX/Unity 包，可拆分部件。发布页元数据写 CC-BY 4.0，正文同时限制原始素材再分发。 | [FRAME] 场景、器械参考优先。许可文字存在不一致，取得明确说明前不把原始资产放入公开仓库。 |
| E2 | [INFERRED｜HIGH] [Poly Ronin — Low Poly Old School Gym Equipment](https://assetstore.unity.com/packages/3d/props/interior/low-poly-old-school-gym-equipment-313032) | [INFERRED｜HIGH] 页面免费，Standard Asset Store EULA，标记兼容 Unity 6000.0.26 和 URP；页面标签涉及凳、架、哑铃。 | [FRAME] 小规模器械候选；完整器械清单、拆件及碰撞体需导入后验证。 |
| E3 | [INFERRED｜HIGH] [iPoly3D Lowpoly Gym Environment](https://ipoly3d.com/lowpoly-gym-environtment/) | [INFERRED｜HIGH] 页面 $4.99，135 模型，Unity/UE/FBX；4 个已绑定角色但无动画。 | [FRAME] 更完整的环境候选；角色不等于已包含训练动作，具体授权以购买版本附带条款核对。 |

## [FRAME] 可编辑动作资源来源

| 编号 | 直接来源 | 核查内容 | 格式、价格与许可 |
|---|---|---|---|
| A1 | [INFERRED｜HIGH] [TricksUp — Barbell Workout Animations](https://www.fab.com/listings/2ac3a2de-3497-47e3-9f9a-203712fbca7d) | [INFERRED｜HIGH] 50 个循环动作，包含本表引用的卧推、深蹲、凳上臀桥等。 | [INFERRED｜HIGH] FBX，发布者写可用于 Unity/UE；当前公开文本未返回价格和具体许可档位，待采购时核查。 |
| A2 | [INFERRED｜HIGH] [TricksUp — Dumbbell Workout Animations](https://www.fab.com/listings/d0c5aa47-7015-4196-92d6-69ebd95cbc88) | [INFERRED｜HIGH] 50 个循环，包含弯举、肩推及前举/侧平举组合。发布者说明 Unity 版本不含哑铃道具。 | [INFERRED｜HIGH] FBX/Unity；价格与具体许可档位公开文本未返回，待核查。 |
| A3 | [INFERRED｜HIGH] [TricksUp — Free Demo](https://www.fab.com/listings/1964ab53-9fef-4661-8bc4-f2ec1cb72378) | [INFERRED｜HIGH] 免费演示包括深蹲与宽握卧推等；没有覆盖本项目全部候选动作。 | [INFERRED｜HIGH] FBX；免费不代表可以脱离游戏分发源文件，需保留领取时的具体许可。 |
| A4 | [INFERRED｜HIGH] [Animpacks — Gym Animation Pack](https://www.fab.com/listings/39841328-c98e-4d8d-b7fd-951cc0f17c00) | [INFERRED｜HIGH] 27 个动作捕捉动画，明确含双手三头下压。附带道具仅作参考，发布者明确尚未优化/贴图。 | [INFERRED｜HIGH] FBX，面向 UE5 Manny 骨架；Unity 需另做映射。公开价格与许可档位未返回，页面另有免费 FBX 样例。 |
| A5 | [INFERRED｜HIGH] [Hancook98 — Lat Pulldown](https://www.cgtrader.com/3d-models/character/human-anatomy/lat-pulldown) | [INFERRED｜HIGH] 已绑定、已动画的人体和下拉器械，页面标约 200,000 polygons。 | [INFERRED｜HIGH] $9.95；FBX/MA/OBJ/BLEND 等，标 Royalty Free License。价格为付费，不是免费；源文件权利范围仍以该商品许可为准。 |

[INFERRED｜HIGH] [Fab Standard License 摘要](https://www.fab.com/eula)允许将已获对应授权的资源集成于作品并使用兼容工具，禁止独立再分发资源；具体商品仍需核对实际许可档位。以上商店页面信息属于检索证据，未完成本工程内的购买包导入测试。

## [FRAME] 视频参考与骨骼资源区分

[INFERRED｜HIGH] [MoveKit Gym App Library](https://movekit.com/packs/gym-app-library)页面为 $249、424 个练习的视频/海报与元数据；[窄握下拉样例](https://movekit.com/exercises/narrow-pulldown)可作镜头与姿态参考。MP4 不能替代本工程所需的人物网格、骨架和动作剪辑，不纳入 3D 模型采购方案。

## [FRAME] 本次已制作的资源

[COMPUTED｜HIGH] 本工程自制 8 套 `prefab`、8 个约 2 秒循环 `anim`、8 个动作场景及公共入口。每套含简化人物、对应器械和动作轨迹；生成源在 `Assets/FitnessSimulator/Editor/PrototypeStageFactory.cs`，可重复生成和修改。

[FRAME] 正式资源接入清单：统一单位/骨架 → 检查双手握持和足底/凳面接触 → 器械与人物动画同步 → 起始、动作、失败、结束阶段 → 肌群高亮材质 → 游戏反馈点 → 横屏镜头 → 移动端性能实测。计分输入、成功失败条件和训练教育文本仍由各玩法需求决定。

[COMPUTED｜HIGH] 当前原型未下载或复制上述第三方网格、纹理和动画文件；所有在线资源保持为参考链接。因此目前无需用户先购买素材即可打开工程验证。
