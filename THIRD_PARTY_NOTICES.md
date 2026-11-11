# 第三方组件与来源说明

本仓库为**自研技能文档与脚本**，不含第三方项目源码。

- 文中提到的 SolidWorks API、SolidCraft、COM/PIA 接口说明来自各自官方文档，版权归 Dassault Systèmes / SolidCraft 作者所有，此处仅作使用说明引用。
- 文中若含对公开示例代码的参考（思路或片段），未逐条标注出处；如权利人认为使用不当，请开 issue 指出具体路径，我会补署名或移除。

_最后更新：2026-11-11_

## 三、引用的第三方技能 / 工具

| 项目 | 上游 | 作者 | 许可 | 关系 |
| --- | --- | --- | --- | --- |
| solidworks-automation-skill | https://github.com/wzyn20051216/solidworks-automation-skill | wzyn20051216 | MIT | `mech-part-pipeline` / `mech-design-optimize` / `solidcraft-verify` 在方法论与流程上参考了该技能；本仓库**不打包**其源码，原文路径在公开版里以 `%DSH_WORKSPACE%\sw-skill\solidworks-automation-skill-main` 表示 |
| SolidCraft | https://github.com/solidcraft（按上游发布为准） | SolidCraft 作者 | MIT / Apache-2.0 | `solidcraft-verify` 为其旁路验算器技能，仅调用本机已安装的 CLI，不分发二进制 |
