"""Export the AzureCTL gene picker from verified JP tables and English translations."""

import argparse
from collections import Counter
from hashlib import sha256
import json
from pathlib import Path
import re
import sys


TABLE_SHA = "16e4df000a3265bf7d150c05f602c689a0cfa1e4f3ba170273f3e30e81fb3915"
VSTRINGS_SHA = "0a31f701c0bce14a62475218698809dba518ae2487aac3cd4afa1f7419fb8d39"
AMOUNTS = re.compile(r"[0-9]+(?:\.[0-9]+)?%?")
FLAT_ATTACK_DEFENSE = re.compile(r"自身の攻撃力が(\d+)上昇し、防御力が(\d+)上昇する。")
TAGS = re.compile(r"<[^<>]+>")


def digest(data):
    return sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--decoder", type=Path, required=True,
                        help="Azurepro server/scripts directory containing native_mmap.py")
    parser.add_argument("--table-data", type=Path, required=True)
    parser.add_argument("--vstrings", type=Path, required=True)
    parser.add_argument("--translations", type=Path, required=True,
                        help="Azur English patch jp-nonstory-draft.jsonl")
    parser.add_argument("--output", type=Path,
                        default=Path(__file__).resolve().parents[1] / "genes.json")
    args = parser.parse_args()
    sys.path.insert(0, str(args.decoder.resolve()))
    from native_mmap import Tables

    tables = Tables(args.table_data, args.vstrings)
    if digest(tables.data) != TABLE_SHA or digest(tables.vstrings) != VSTRINGS_SHA:
        raise ValueError("Expected the official JP 3219598 MMap export")

    source = args.translations.read_bytes()
    translations = {}
    for line in source.decode("utf-8-sig").splitlines():
        row = json.loads(line)
        if row["h"] != digest(row["ja"].encode("utf-8"))[:16] or row["ja"] in translations:
            raise ValueError("Invalid English patch translation record")
        translations[row["ja"]] = row["en"]

    def rows(name):
        return tables.read(name)["rows"]

    def language(name):
        return {str(row["id"]): row["value"] for row in tables.read(name, "jp")["rows"]}

    names = language("pet_dna")
    descriptions = language("skill_level")
    authored = {row["id"]: row for row in rows("pet_dna")}
    items = {row["id"]: row for row in rows("common_item")}
    levels = {row["skillId"]: row for row in rows("skill_level") if row["level"] == 1}
    fruits = sorted((row for row in rows("pet_feed_item") if row["type"] == 3),
                    key=lambda row: row["id"])
    if len(fruits) != 102 or len({row["dnaParam"] for row in fruits}) != len(fruits):
        raise ValueError("Unexpected JP gene fruit roster")

    genes = []
    for fruit in fruits:
        gene_id, fruit_id = fruit["dnaParam"], fruit["id"]
        gene = authored[gene_id]
        if gene["rarity"] not in (3, 4, 5) or items[fruit_id]["subType"] != 32 or fruit["isReuse"]:
            raise ValueError(f"Unexpected gene fruit {fruit_id}")
        name = names[gene["name"]]
        description = descriptions[levels[gene_id]["skillDescribe"]]
        effect = translations.get(description)
        if effect is None:
            match = FLAT_ATTACK_DEFENSE.fullmatch(description)
            if not match:
                raise ValueError(f"Missing English effect for gene {gene_id}")
            effect = f"Increases own ATK by {match[1]} and DEF by {match[2]}."
        effect = TAGS.sub("", effect)
        if Counter(AMOUNTS.findall(description)) != Counter(AMOUNTS.findall(effect)):
            raise ValueError(f"Changed effect amounts or units for gene {gene_id}")
        genes.append({
            "id": gene_id, "name": translations.get(name, name), "effect": effect,
            "fruitId": fruit_id, "rarity": gene["rarity"],
        })

    catalog = {
        "revision": "3219598",
        "sources": {
            "tableDataSha256": TABLE_SHA,
            "globalVStringsSha256": VSTRINGS_SHA,
            "englishTranslationsSha256": digest(source),
        },
        "genes": genes,
    }
    args.output.write_bytes((json.dumps(catalog, ensure_ascii=False, indent=2) + "\n").encode("utf-8"))


if __name__ == "__main__":
    main()
