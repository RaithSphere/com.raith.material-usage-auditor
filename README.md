# Material Usage Auditor

Material Usage Auditor is a read-only Unity Editor tool for checking the materials and texture import limits actually used by a model.

## Open the tool

Choose **Tools > Material Usage Auditor**.

## Use it

1. Assign a root GameObject, or select one in the Hierarchy and click **Use Selection**.
2. Click **Scan Model**.
3. Expand a material to see its renderer slots and assigned texture properties.

The scan includes inactive child objects. It reads `sharedMaterials`, so it does not instantiate materials or change the scene. Texture rows show source dimensions, imported dimensions, the default Max Size, the effective Max Size for the active build target, and Unity's runtime memory size for the texture in the Editor. The summary reports a deduplicated texture-memory total for the scanned model. Memory size reflects the current Editor import/runtime representation and can differ from memory on another build target or device. An active platform override is labelled explicitly.

Click **Select** on any texture row to select and ping that asset. Its Import Settings will appear in the Inspector, where you can change Max Size and apply the new setting.

Use **Export CSV** when you need a portable report. Exporting does not change project assets.

## AI Disclosure

AI tools assisted with development and documentation of this package. The maintainer reviews and is responsible for the published code and content.
