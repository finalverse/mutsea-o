"""Data fidelity regressions for the bounded SQLite -> PostgreSQL adapter."""
import csv
import unittest
from migrate_sqlite import cell, quote

class TransferTests(unittest.TestCase):
    def column(self, kind, name="Data"):
        return {"data_type": kind, "column_name": name}

    def test_null_is_distinct_from_empty_text(self):
        self.assertEqual(cell(None, self.column("text")), r"\N")
        self.assertEqual(cell("", self.column("text")), '""')

    def test_text_quotes_newlines_and_literal_null_marker(self):
        for value in ('a,"b"\nc', r"\N", "λ world"):
            self.assertEqual(next(csv.reader([cell(value, self.column("text"))]))[0], value)

    def test_binary_bytes_are_lossless(self):
        self.assertEqual(cell(bytes(range(256)), self.column("bytea")), '"\\x' + bytes(range(256)).hex() + '"')

    def test_native_base64_fields_decode_without_changing_payload(self):
        self.assertEqual(cell("AP8=", self.column("bytea", "ParticleSystem")), '"\\x00ff"')
        self.assertEqual(cell("", self.column("bytea", "TextureAnimation")), '"\\x"')

    def test_unknown_binary_text_is_rejected(self):
        with self.assertRaises(ValueError):
            cell("AP8=", self.column("bytea", "Data"))

    def test_all_native_boolean_forms(self):
        for value in (True, 1, "True", "1"):
            self.assertEqual(cell(value, self.column("boolean")), '"t"')
        for value in (False, 0, "False", "0"):
            self.assertEqual(cell(value, self.column("boolean")), '"f"')

    def test_invalid_boolean_is_rejected(self):
        for value in (2, "yes", ""):
            with self.assertRaises(ValueError):
                cell(value, self.column("boolean"))

    def test_identifiers_cannot_inject_sql(self):
        self.assertEqual(quote("StartStr"), '"StartStr"')
        for name in ('x";DROP TABLE prims', "x.y", ""):
            with self.assertRaises(ValueError):
                quote(name)

if __name__ == "__main__":
    unittest.main()
