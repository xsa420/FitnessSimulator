# [FRAME] TEMP关系布局代理接入清单

[COMPUTED｜HIGH] 本目录不参与编译；仅准备GameUiSmoke.RelationshipLayout.cs.utmp，未修改Assets，未生成图片。待架构师释放后由UI owner将其作为Editor/PlayerBuild/GameUiSmoke.RelationshipLayout.cs接入；GameUiSmoke主类改partial。不可在当前冻结窗口搬入。

[FRAME] 新独立开关`-fitnessRelationshipLayoutSmoke`，沿用原GameUiSmoke.Run及-fitnessUiSmokeExit入口。未带开关时保持原15图与真实会话流程，带开关时只要求5张关系布局图，不强制旧15图。尺寸640×320，字号默认与1.25；不宣称手机安全区证据。

[FRAME] 主文件最小接线：StartInPlayMode判断命令行开关，设置relationshipLayout为新夹具（未开启则null）、relationshipLayoutStep=0；Initialize以夹具或原session为IGameFrontend；RenderTexture按模式选640×320或原1280×720。Tick在常规会话分支前检查relationshipLayout!=null时调用TickRelationshipLayout并return。Finish按模式选择RelationshipCaptures或RequiredCaptures。原真实会话仍保留打开/释放逻辑，不对其提交布局状态。

[FRAME] Evidence新增scenario与layoutFixture字段，关系模式分别为RelationshipLayoutOnly和true；业务规则验证字段businessRulesVerified=false，保持source=Test/inputSource=ScriptReplay/platform=EditorGraphicsProxy/androidTouchVerified=false；明确无真实训练核、无业务进展、无关系公开规则证明。组内入口额外用直接回调检查防护时，在inputEvents标synthetic-callback，不冒充指针操作。截图本身使用原实际uGUI/Camera采集，返回按钮仍经过现可见性与顶层raycast检查。

[FRAME] 新capture名：relationships-narrow-default-top、relationships-narrow-default-bottom、relationships-narrow-large-top、relationships-narrow-large-bottom、relationships-training-aux-disabled。默认／大字均真实滚动到底，确认返回按钮可见且可点；组内检查角色／计划／安排按钮禁用及回调守卫。截图须人工局部排版复看，程序Passed只代表这些断言。

[FRAME] 夹具内容全标布局占位，不代替应用层D1/D7/D8身份、99/100好感、0/1书签或冷却日期样本。这些业务公开规则由应用测试负责。无需改生产View，无需启动全量旧图或手机流程。

[COMPUTED｜HIGH] 已备完整主文件GameUiSmoke.Main.cs.utmp和partial副本。独立Roslyn编译以现有项目程序集及编辑器托管引用验证二者引用闭合，输出仅本TEMP目录RelationshipLayoutCompile.dll；未启动编辑器、未运行夹具。编译参数保存在RelationshipLayoutCompile.rsp，诊断保存RelationshipLayoutCompile.log；独立编译不代替后续正式导入或五图验收。正式接入须复核主文件相对最新Assets变化，避免覆盖其他后续改动。

[RULES I BROKE]: 无。
