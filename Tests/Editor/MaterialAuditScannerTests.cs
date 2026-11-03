using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Raith.MaterialUsageAuditor.Tests
{
    internal sealed class MaterialAuditScannerTests
    {
        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var index = createdObjects.Count - 1; index >= 0; index--)
            {
                if (createdObjects[index] != null)
                    Object.DestroyImmediate(createdObjects[index]);
            }
            createdObjects.Clear();
        }

        [Test]
        public void Scan_IncludesInactiveChildRenderers()
        {
            var root = CreateGameObject("Root");
            var child = CreateRenderer("Inactive Child", root.transform);
            child.gameObject.SetActive(false);
            child.sharedMaterial = CreateMaterial("Material");

            var result = MaterialAuditScanner.Scan(root);

            Assert.That(result.RendererCount, Is.EqualTo(1));
            Assert.That(result.Materials, Has.Count.EqualTo(1));
            Assert.That(result.Materials[0].Usages[0].HierarchyPath, Is.EqualTo("Root/Inactive Child"));
        }

        [Test]
        public void Scan_DeduplicatesMaterialsAndTexturesWhileKeepingEveryUsage()
        {
            var root = CreateGameObject("Root");
            var material = CreateMaterial("Shared Material");
            var texture = CreateTexture("Shared Texture");
            material.mainTexture = texture;
            CreateRenderer("First", root.transform).sharedMaterial = material;
            CreateRenderer("Second", root.transform).sharedMaterial = material;

            var result = MaterialAuditScanner.Scan(root);

            Assert.That(result.Materials, Has.Count.EqualTo(1));
            Assert.That(result.Materials[0].Usages, Has.Count.EqualTo(2));
            Assert.That(result.UniqueTextureCount, Is.EqualTo(1));
        }

        [Test]
        public void Scan_CountsNullMaterialSlotsWithoutCreatingEntries()
        {
            var root = CreateGameObject("Root");
            var renderer = CreateRenderer("Mesh", root.transform);
            renderer.sharedMaterials = new[] { CreateMaterial("Material"), null };

            var result = MaterialAuditScanner.Scan(root);

            Assert.That(result.EmptyMaterialSlotCount, Is.EqualTo(1));
            Assert.That(result.Materials, Has.Count.EqualTo(1));
        }

        [Test]
        public void Scan_ReportsNonImportedTextureDimensionsWithoutClaimingAnImportLimit()
        {
            var root = CreateGameObject("Root");
            var material = CreateMaterial("Material");
            var texture = CreateTexture("Runtime Texture", 64, 32);
            material.mainTexture = texture;
            CreateRenderer("Mesh", root.transform).sharedMaterial = material;

            var result = MaterialAuditScanner.Scan(root);
            var auditedTexture = result.Materials[0].Textures[0];

            Assert.That(auditedTexture.ImportedWidth, Is.EqualTo(64));
            Assert.That(auditedTexture.ImportedHeight, Is.EqualTo(32));
            Assert.That(auditedTexture.HasImporter, Is.False);
            Assert.That(auditedTexture.EffectiveMaxSize, Is.Zero);
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            createdObjects.Add(gameObject);
            return gameObject;
        }

        private MeshRenderer CreateRenderer(string name, Transform parent)
        {
            var gameObject = CreateGameObject(name);
            gameObject.transform.SetParent(parent);
            return gameObject.AddComponent<MeshRenderer>();
        }

        private Material CreateMaterial(string name)
        {
            var material = new Material(Shader.Find("Standard")) { name = name };
            createdObjects.Add(material);
            return material;
        }

        private Texture2D CreateTexture(string name, int width = 16, int height = 16)
        {
            var texture = new Texture2D(width, height) { name = name };
            createdObjects.Add(texture);
            return texture;
        }
    }
}
