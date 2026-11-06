# Material Usage Auditor

Material Usage Auditor is a read-only Unity Editor tool for checking the materials and texture import limits actually used by a model.

## Open the tool

Choose **Tools > Material Usage Auditor**.

## Use it

1. Assign a root GameObject, or select one in the Hierarchy and click **Use Selection**.
2. Click **Scan Model**.
3. Click **Select Object** on a material row to select the first GameObject using it, or expand the material to choose a specific renderer object and see its assigned texture properties.

The scan includes inactive child objects. It reads `sharedMaterials`, so it does not instantiate materials or change the scene. Texture rows show source dimensions, imported dimensions, the default Max Size, the effective Max Size for the active build target, and Unity's runtime memory size for the texture in the Editor. The summary reports a deduplicated Editor-memory total; unavailable readings are called out and excluded. This is the current Editor representation, not a build-target memory prediction, and can differ on a device. An active platform override is labelled explicitly.

Sort the material list by Editor memory, maximum texture size, or renderer usage. Filters can show textures at or above the selected size threshold, textures without import settings or an asset path, and materials with no assigned textures.

Click **Select** on any texture row to select and ping that asset. Its Import Settings will appear in the Inspector, where you can change Max Size and apply the new setting.

Use **Export CSV** when you need a portable report. It records the UTC scan time and Editor memory in bytes, and includes a separate unique-texture summary to avoid counting shared textures more than once. Exporting does not change project assets.

## AI Disclosure

AI tools assisted with development and documentation of this package. The maintainer reviews and is responsible for the published code and content.
