import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

MODULE_PATH = Path(__file__).parents[1] / "tools" / "sync_bwiki_reference.py"
SPEC = importlib.util.spec_from_file_location("sync_bwiki_reference", MODULE_PATH)
sync = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = sync
SPEC.loader.exec_module(sync)


class ParserTests(unittest.TestCase):
    def test_nested_template_link_pipe_comment_and_nowiki(self):
        text = "{{技能/武将|技能名=甲|经典技能描述={{版本比较|增改|A=B}} [[牌|显示]] <!-- |x --> <nowiki>|y=z</nowiki>}}"
        templates, anomalies = sync.parse_templates(text)
        self.assertEqual([], anomalies)
        self.assertEqual("技能/武将", templates[0].name)
        self.assertIn("[[牌|显示]]", templates[0].fields["经典技能描述"])
        self.assertIn("<nowiki>|y=z</nowiki>", templates[0].fields["经典技能描述"])

    def test_duplicate_skill_history_templates_are_separate(self):
        text = "{{技能/武将|技能名=武魂|技能版本=经典|经典技能描述=今}}{{技能/武将|技能名=武魂|历史版本=正式服I版|经典技能描述=昔}}"
        templates, anomalies = sync.parse_templates(text)
        self.assertFalse(anomalies)
        self.assertEqual(2, len(templates))
        self.assertEqual("经典", templates[0].fields["技能版本"])
        self.assertEqual("正式服I版", templates[1].fields["历史版本"])

    def test_category_continuation_and_missing_page(self):
        with tempfile.TemporaryDirectory() as directory:
            client = sync.Client(Path(directory), 0, 0, 0, False)
            responses = [
                {"query": {"categorymembers": [{"pageid": 1, "title": "甲"}]}, "continue": {"cmcontinue": "x", "continue": "-||"}},
                {"query": {"categorymembers": [{"pageid": 2, "title": "乙"}]}},
            ]
            with mock.patch.object(client, "get", side_effect=responses):
                members, pages = sync.get_category(client, "分类:测试", "test")
            self.assertEqual(2, len(members))
            self.assertEqual(2, len(pages))
            with mock.patch.object(client, "get", return_value={"query": {"pages": [{"title": "甲", "missing": True}]}}):
                fetched, failures = sync.get_pages(client, [{"pageid": 1, "title": "甲"}], "test")
            self.assertEqual(1, len(fetched))
            self.assertEqual("missing page or revision", failures[0]["reason"])

    def test_partial_batch_response_records_omitted_pageid(self):
        with tempfile.TemporaryDirectory() as directory:
            client = sync.Client(Path(directory), 0, 0, 0, False)
            returned = {"query": {"pages": [{"pageid": 1, "title": "甲", "revisions": [{"revid": 1}]}]}}
            members = [{"pageid": 1, "title": "甲"}, {"pageid": 2, "title": "乙"}]
            with mock.patch.object(client, "get", return_value=returned):
                _, failures = sync.get_pages(client, members, "test")
            self.assertEqual([2], [failure["pageid"] for failure in failures])
            self.assertEqual("page omitted from API batch response", failures[0]["reason"])

    def test_category_cache_key_includes_continuation_params(self):
        with tempfile.TemporaryDirectory() as directory:
            client = sync.Client(Path(directory), 0, 0, 0, False)
            response = mock.MagicMock()
            response.__enter__.return_value = response
            response.__exit__.return_value = False
            response.read.return_value = json.dumps({"query": {"categorymembers": []}}).encode()
            with mock.patch("urllib.request.urlopen", return_value=response):
                client.get({"action": "query", "cmcontinue": "first"}, "category-test-2")
                client.get({"action": "query", "cmcontinue": "second"}, "category-test-2")
            self.assertEqual(2, len(list(Path(directory).glob("category-test-2-*.json"))))

    def test_api_error_is_not_cached_as_success(self):
        with tempfile.TemporaryDirectory() as directory:
            client = sync.Client(Path(directory), 0, 0, 0, False)
            response = mock.MagicMock()
            response.__enter__.return_value = response
            response.__exit__.return_value = False
            response.read.return_value = json.dumps({"error": {"code": "bad"}}).encode()
            with mock.patch("urllib.request.urlopen", return_value=response):
                with self.assertRaises(sync.ApiError):
                    client.get({"action": "query"}, "failure")
            self.assertFalse((Path(directory) / "failure.json").exists())

    def test_fresh_and_cached_api_data_are_identical(self):
        with tempfile.TemporaryDirectory() as directory:
            payload = {
                "query": {
                    "pages": [{
                        "pageid": 1,
                        "revisions": [{"slots": {"main": {"content": "{{武将|武将名=甲|规则集问答=A&amp;B}}"}}}],
                    }]
                }
            }
            response = mock.MagicMock()
            response.__enter__.return_value = response
            response.__exit__.return_value = False
            response.read.return_value = json.dumps(payload).encode()
            params = {"action": "query", "titles": "甲"}
            fresh_client = sync.Client(Path(directory), 0, 0, 0, False)
            with mock.patch("urllib.request.urlopen", return_value=response):
                fresh = fresh_client.get(params, "pages-generals-001")
            cached = sync.Client(Path(directory), 0, 0, 0, False).get(params, "pages-generals-001")
            self.assertEqual(fresh, cached)
            self.assertEqual("{{武将|武将名=甲|规则集问答=A&amp;B}}", cached["query"]["pages"][0]["revisions"][0]["slots"]["main"]["content"])


class MainPublicationTests(unittest.TestCase):
    def _page(self):
        return {"pageid": 1, "ns": 0, "title": "甲", "revisions": [{"revid": 2, "parentid": 1,
                "timestamp": "2026-01-01T00:00:00Z", "slots": {"main": {"content": "{{武将|武将名=甲|经典体力=4}}"}}}]}

    def test_failed_batch_preserves_all_published_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "bwiki"
            (output / "pages").mkdir(parents=True)
            paths = [output / "category-generals.json", output / "category-skills.json", output / "generals.json",
                     output / "skills.json", output / "manifest.json", output / "pages" / "1.json"]
            for index, path in enumerate(paths):
                path.write_bytes(f"sentinel-{index}".encode())
            before = {path: path.read_bytes() for path in paths}
            members = [{"pageid": 1, "ns": 0, "title": "甲"}]
            with mock.patch.object(sync, "get_category", side_effect=[(members, []), (members, [])]), \
                 mock.patch.object(sync, "get_pages", side_effect=[([self._page()], [{"pageid": 9, "reason": "omitted"}]), ([self._page()], [])]):
                result = sync.main(["--output", str(output), "--delay-min", "0", "--delay-max", "0"])
            self.assertEqual(2, result)
            self.assertEqual(before, {path: path.read_bytes() for path in paths})
            self.assertTrue((output / "last-attempt.json").exists())

    def test_success_publishes_staged_snapshot(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "bwiki"
            members = [{"pageid": 1, "ns": 0, "title": "甲"}]
            skill_page = {**self._page(), "title": "技", "pageid": 2,
                          "revisions": [{"revid": 3, "timestamp": "2026-01-01T00:00:00Z", "slots": {"main": {"content": "{{技能/武将|技能名=技|所属武将=甲|技能版本=经典|经典技能描述=说明}}"}}}]}
            skill_members = [{"pageid": 2, "ns": 0, "title": "技"}]
            with mock.patch.object(sync, "get_category", side_effect=[(members, []), (skill_members, [])]), \
                 mock.patch.object(sync, "get_pages", side_effect=[([self._page()], []), ([skill_page], [])]):
                result = sync.main(["--output", str(output), "--delay-min", "0", "--delay-max", "0"])
            self.assertEqual(0, result)
            self.assertEqual("甲", json.loads((output / "generals.json").read_text(encoding="utf-8"))[0]["name"])
            self.assertEqual("技", json.loads((output / "skills.json").read_text(encoding="utf-8"))[0]["name"])
            self.assertTrue(json.loads((output / "manifest.json").read_text(encoding="utf-8"))["complete"])
            self.assertEqual("succeeded", json.loads((output / "last-attempt.json").read_text(encoding="utf-8"))["status"])

    def test_publication_io_failure_does_not_claim_snapshot_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "bwiki"
            output.mkdir()
            (output / "manifest.json").write_text("previous manifest", encoding="utf-8")
            members = [{"pageid": 1, "ns": 0, "title": "甲"}]
            original_replace = Path.replace

            def fail_after_page_publish(path, target):
                if Path(target).name == "category-generals.json":
                    raise OSError("simulated publication failure")
                return original_replace(path, target)

            with mock.patch.object(sync, "get_category", side_effect=[(members, []), ([], [])]), \
                 mock.patch.object(sync, "get_pages", side_effect=[([self._page()], []), ([], [])]), \
                 mock.patch.object(Path, "replace", fail_after_page_publish):
                result = sync.main(["--output", str(output), "--delay-min", "0", "--delay-max", "0"])
            self.assertEqual(1, result)
            self.assertTrue((output / "pages" / "1.json").exists())
            attempt = json.loads((output / "last-attempt.json").read_text(encoding="utf-8"))
            self.assertFalse(attempt["publishedSnapshotPreserved"])
            self.assertTrue(attempt["publicationStarted"])
            self.assertEqual("previous manifest", (output / "manifest.json").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
