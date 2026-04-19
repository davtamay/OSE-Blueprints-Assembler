using System.Reflection;
using NUnit.Framework;
using OSE.Content;
using OSE.Editor;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Round-trip coverage for <see cref="TaskJsonSerializer"/>. Two hand-rolled
    /// TTAW serializers (one for tool actions, one for taskOrder entries) were
    /// silently dropping authored fields every time an author hit Save — a
    /// bug that destroyed Phase I content three separate times. These tests
    /// lock the invariant: every public field on <see cref="ToolActionDefinition"/>
    /// and <see cref="TaskOrderEntry"/> round-trips cleanly through the
    /// serializer and back into an equivalent object.
    ///
    /// The reflection-based "every field is covered" tests are the backstop:
    /// adding a new public field on either source type without updating the
    /// serializer will fail those tests loudly, instead of silently stripping
    /// on the next save.
    /// </summary>
    [TestFixture]
    public class TaskJsonSerializerTests
    {
        // ── ToolActionDefinition round-trip ───────────────────────────────

        [Test]
        public void ToolAction_AllFieldsRoundTrip()
        {
            var original = new ToolActionDefinition
            {
                id             = "action_demo",
                toolId         = "tool_power_drill",
                actionType     = "tighten",
                targetId       = "target_demo",
                requiredCount  = 3,
                successMessage = "Bolt seated — nice \"tap\" needed.",  // embedded quotes test JsonQuote
                failureMessage = "Re-seat and retry.",
                interaction    = new ToolPartInteraction
                {
                    archetype  = "lerp",
                    easing     = "easeInOut",
                    followPart = true
                }
            };

            string json = TaskJsonSerializer.BuildToolActionJson(original);
            var roundTripped = JsonUtility.FromJson<ToolActionDefinition>(json);

            Assert.AreEqual(original.id,             roundTripped.id);
            Assert.AreEqual(original.toolId,         roundTripped.toolId);
            Assert.AreEqual(original.actionType,     roundTripped.actionType);
            Assert.AreEqual(original.targetId,       roundTripped.targetId);
            Assert.AreEqual(original.requiredCount,  roundTripped.requiredCount);
            Assert.AreEqual(original.successMessage, roundTripped.successMessage);
            Assert.AreEqual(original.failureMessage, roundTripped.failureMessage);
            Assert.IsNotNull(roundTripped.interaction, "interaction payload must round-trip");
            Assert.AreEqual(original.interaction.archetype,  roundTripped.interaction.archetype);
            Assert.AreEqual(original.interaction.easing,     roundTripped.interaction.easing);
            Assert.AreEqual(original.interaction.followPart, roundTripped.interaction.followPart);
        }

        [Test]
        public void ToolAction_NullInteraction_RoundTripsAsNull()
        {
            var original = new ToolActionDefinition
            {
                id            = "action_no_interaction",
                toolId        = "tool_hand",
                actionType    = "tighten",
                targetId      = "target_demo",
                requiredCount = 1,
                interaction   = null
            };

            string json = TaskJsonSerializer.BuildToolActionJson(original);
            var roundTripped = JsonUtility.FromJson<ToolActionDefinition>(json);

            // Omitted interaction → FromJson leaves it null (the JSON has no
            // "interaction" key to populate from).
            Assert.IsNull(roundTripped.interaction);
        }

        [Test]
        public void ToolAction_Null_EmitsEmptyObject()
        {
            Assert.AreEqual("{}", TaskJsonSerializer.BuildToolActionJson(null));
        }

        [Test]
        public void ToolAction_EveryPublicFieldIsReferencedBySerializer()
        {
            // Reflection guard: when a new public field is added to
            // ToolActionDefinition, this test fails unless the new field name
            // appears in the serializer's JSON output for a populated instance.
            // Keeps the "add a field → update the serializer" rule mechanical.
            var populated = new ToolActionDefinition
            {
                id             = "id",
                toolId         = "toolId",
                actionType     = "actionType",
                targetId       = "targetId",
                requiredCount  = 7,
                successMessage = "s",
                failureMessage = "f",
                interaction    = new ToolPartInteraction { archetype = "lerp" }
            };
            string json = TaskJsonSerializer.BuildToolActionJson(populated);

            var fields = typeof(ToolActionDefinition).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var f in fields)
            {
                StringAssert.Contains($"\"{f.Name}\":", json,
                    $"ToolActionDefinition field '{f.Name}' is not emitted by BuildToolActionJson. "
                    + "Either add it to the serializer or mark it non-public.");
            }
        }

        // ── TaskOrderEntry round-trip ─────────────────────────────────────

        [Test]
        public void TaskOrderEntry_AllFieldsRoundTrip()
        {
            var original = new TaskOrderEntry
            {
                kind          = "toolAction",
                id            = "action_demo",
                isOptional    = true,
                unorderedSet  = "panel_bars",
                endTransform  = new TaskEndTransform
                {
                    position = new SceneFloat3 { x = 1.1f, y = 2.2f, z = 3.3f },
                    rotation = new SceneQuaternion { x = 0.1f, y = 0.2f, z = 0.3f, w = 0.9f },
                    scale    = new SceneFloat3 { x = 1.0f, y = 1.0f, z = 1.0f }
                }
            };

            string json = TaskJsonSerializer.BuildTaskOrderEntryJson(original);
            var roundTripped = JsonUtility.FromJson<TaskOrderEntry>(json);

            Assert.AreEqual(original.kind,         roundTripped.kind);
            Assert.AreEqual(original.id,           roundTripped.id);
            Assert.AreEqual(original.isOptional,   roundTripped.isOptional);
            Assert.AreEqual(original.unorderedSet, roundTripped.unorderedSet);
            Assert.IsNotNull(roundTripped.endTransform, "endTransform must round-trip");
            Assert.AreEqual(original.endTransform.position.x, roundTripped.endTransform.position.x);
            Assert.AreEqual(original.endTransform.rotation.w, roundTripped.endTransform.rotation.w);
            Assert.AreEqual(original.endTransform.scale.y,    roundTripped.endTransform.scale.y);
        }

        [Test]
        public void TaskOrderEntry_DefaultsOmittedFromJson()
        {
            // The common "strict-sequential non-optional singleton" row stays
            // compact: only kind + id emitted.
            var e = new TaskOrderEntry { kind = "part", id = "bolt_a" };
            string json = TaskJsonSerializer.BuildTaskOrderEntryJson(e);

            Assert.IsTrue(json.Contains("\"kind\":\"part\""));
            Assert.IsTrue(json.Contains("\"id\":\"bolt_a\""));
            Assert.IsFalse(json.Contains("\"isOptional\""),
                "isOptional must not emit when false — keeps the common row compact");
            Assert.IsFalse(json.Contains("\"unorderedSet\""),
                "empty unorderedSet must be omitted");
            Assert.IsFalse(json.Contains("\"endTransform\""),
                "null endTransform must be omitted");
        }

        [Test]
        public void TaskOrderEntry_Null_EmitsEmptyObject()
        {
            Assert.AreEqual("{}", TaskJsonSerializer.BuildTaskOrderEntryJson(null));
        }

        [Test]
        public void TaskOrderEntry_EveryPublicFieldIsReferencedBySerializer()
        {
            // Same reflection guard as the ToolAction version.
            var populated = new TaskOrderEntry
            {
                kind          = "part",
                id            = "p",
                isOptional    = true,
                unorderedSet  = "set",
                endTransform  = new TaskEndTransform
                {
                    position = new SceneFloat3 { x = 1f },
                    rotation = new SceneQuaternion { w = 1f },
                    scale    = new SceneFloat3 { x = 1f, y = 1f, z = 1f }
                }
            };
            string json = TaskJsonSerializer.BuildTaskOrderEntryJson(populated);

            var fields = typeof(TaskOrderEntry).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var f in fields)
            {
                StringAssert.Contains($"\"{f.Name}\":", json,
                    $"TaskOrderEntry field '{f.Name}' is not emitted by BuildTaskOrderEntryJson. "
                    + "Either add it to the serializer or mark it non-public.");
            }
        }

        // ── JsonQuote ─────────────────────────────────────────────────────

        [Test]
        public void JsonQuote_EscapesQuotesAndControlChars()
        {
            Assert.AreEqual("\"\"",                TaskJsonSerializer.JsonQuote(""));
            Assert.AreEqual("\"hello\"",           TaskJsonSerializer.JsonQuote("hello"));
            Assert.AreEqual("\"say \\\"hi\\\"\"", TaskJsonSerializer.JsonQuote("say \"hi\""));
            Assert.AreEqual("\"line\\nbreak\"",    TaskJsonSerializer.JsonQuote("line\nbreak"));
            Assert.AreEqual("\"back\\\\slash\"",  TaskJsonSerializer.JsonQuote("back\\slash"));
            Assert.AreEqual("\"\\u0001\"",         TaskJsonSerializer.JsonQuote("\u0001"));
        }
    }
}
