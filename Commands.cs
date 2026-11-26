namespace AzureCTL;

sealed record Arg(string Key, string Label, string Default, string[]? Choices=null);
sealed record Operation(string Name, string[] Template, Arg[] Fields, string Description="", bool NeedsSelection=true)
{
    public override string ToString()=>Name;
    public string[] Expand(string selected,IReadOnlyDictionary<string,string> values)=>Template.Select(t=>t=="{id}"?selected:t.StartsWith('{')?values[t[1..^1]]:t).ToArray();
}
sealed record Section(string Name,string Subtitle,string Category,Operation[] Operations);

static class Commands
{
    public static bool IsAddMissingKibos(IReadOnlyList<string> command)=>command is ["pet","add-missing","max"];

    static Arg N(string key,string label,string initial)=>new(key,label,initial);
    static Arg C(string key,string label,string initial,params string[] values)=>new(key,label,initial,values);
    static Operation Op(string name,string template,string description,params Arg[] fields)=>new(name,template.Split(' '),fields,description);
    public static readonly Section[] Sections =
    [
        new("Characters","Levels, Talent, Runes, Star Blessing and skills.","hero",
        [
            Op("Max character","hero max {id}","Maxes the selected owned character within the official progression limits."),
            Op("Set level","hero set {id} level {value}","The current Talent rank must support the chosen level.",N("value","CHARACTER LEVEL · 1–100","100")),
            Op("Set Talent","hero set {id} talent {value}","Uses official Talent milestones and their level prerequisites.",C("value","TALENT RANK","12",Enumerable.Range(1,12).Select(x=>x.ToString()).ToArray())),
            Op("Set Star Blessing","hero set {id} grade {value}","Sets the character's authored Star Blessing progression.",C("value","STAR BLESSING","6",Enumerable.Range(0,7).Select(x=>x.ToString()).ToArray())),
            Op("Set all skills","hero set {id} skills {value}","Fixed passives keep their authored level; progression prerequisites still apply.",C("value","SKILL LEVEL","max",["max",..Enumerable.Range(1,12).Select(x=>x.ToString())])),
            Op("Set one skill","hero set {id} skill {slot} {value}","Use Inspect to see the selected character's real skill slots and caps.",C("slot","SKILL SLOT","1","1","2","3","4","5","6"),N("value","SKILL LEVEL","12")),
            Op("Set all Runes","hero set {id} runes {value}","Applies to the current Talent rank's authored Rune slots.",C("value","RUNES","all","all","none")),
            Op("Set one Rune","hero set {id} rune {slot} {value}","Sets one authored Rune slot in the current Talent rank.",C("slot","RUNE SLOT","1","1","2","3","4","5"),C("value","STATE","1","1","0")),
            Op("Set bond","hero set {id} favor {value} {exp}","Sets character bond using official levels and EXP limits.",N("value","BOND LEVEL · 0–10","10"),N("exp","BOND EXP","0")),
            Op("Set EXP","hero set {id} exp {value}","Sets EXP within the current character level.",N("value","CHARACTER EXP","0")),
            Op("Heal character","hero heal {id}","Restores the selected character's HP and energy."),
            Op("4★ C6 · 5★ C0","hero rarity-preset","All owned characters: 4-star Star Blessing 6; 5-star Star Blessing 0. Other rarities unchanged.") with {NeedsSelection=false},
        ]),
        new("Unlocking","Browse the character catalog and unlock a character.","heroes",
        [Op("Unlock character","hero unlock {id}","Adds the character if not already owned. Existing progression is retained.")]),
        new("Gear","Equipment enhancement, star grade, quality and ownership.","gear",
        [
            Op("Max equipment","gear max {id}","Uses the selected equipment's official enhancement, grade and quality limits."),
            Op("Set level","gear set {id} level {value}","Equipment caps depend on its configuration. Inspect shows the exact limit.",N("value","ENHANCEMENT LEVEL","15")),
            Op("Set star grade","gear set {id} grade {value}","Uses the official star-grade range for this item.",N("value","STAR GRADE","3")),
            Op("Set quality","gear set {id} quality {value}","Quality follows the JP equipment tables.",C("value","QUALITY ID","5","0","1","2","3","4","5")),
            Op("Equip","gear equip {id} {hero}","Transfers this item to the selected character's matching slot.",N("hero","CHARACTER ID","107002")),
            Op("Clone equipment","gear clone {id}","Creates another owned instance of this equipment."),
            Op("Lock equipment","gear lock {id} {value}","Sets the inventory lock.",C("value","LOCK","1","1","0")),
            Op("Unequip slot","gear unequip {hero} {slot}","Slots: 1 weapon, 2 coat, 3 pants, 4 earring, 5 ring.",N("hero","CHARACTER ID","107002"),C("slot","SLOT","all","all","1","2","3","4","5")) with {NeedsSelection=false},
            Op("Remove equipment","gear remove {id}","Removes an unequipped item from the active save."),
        ]),
        new("Create gear","Choose an equipment configuration from the official catalog.","gear-catalog",
        [Op("Create equipment","gear create {id} {value}","JP equipment has fixed attributes, with no random substat pool. Choose the configuration, then its enhancement level.",N("value","ENHANCEMENT LEVEL","1"))]),
        new("Anima","Anima levels, rank, passive refinement and equipment.","soul",
        [
            Op("Max Anima","soul max {id}","Maxes this Anima within its official limits."),
            Op("Set level","soul set {id} level {value}","The current rank must support the chosen level.",N("value","ANIMA LEVEL","100")),
            Op("Set rank","soul set {id} rank {value}","Uses the selected Anima's official rank range.",N("value","ANIMA RANK","6")),
            Op("Set passive stars","soul set {id} stars {value}","Uses the authored passive refinement limit.",N("value","PASSIVE STARS","5")),
            Op("Equip Anima","soul equip {id} {hero}","Checks the character's role and the Anima's compatibility.",N("hero","CHARACTER ID","107002")),
            Op("Unequip Anima","soul unequip {hero}","Removes the character's currently equipped Anima.",N("hero","CHARACTER ID","107002")) with {NeedsSelection=false},
            Op("4★ R5 · 5★ R1","soul rarity-preset","All owned Anima: 4-star refinement 5; 5-star refinement 1. Level, ascension rank and other rarities unchanged.") with {NeedsSelection=false},
        ]),
        new("Create Anima","Browse the official Anima catalog.","souls",
        [Op("Create Anima","soul create {id}","Adds the selected Anima configuration to this player's inventory.")]),
        new("Kibo","Owned Kibo progression, grades, aptitudes, genes and Star Soul.","pet",
        [
            Op("Set level","pet set {id} level {value}","Kibo levels follow account-level prerequisites and the official cap.",N("value","KIBO LEVEL · 1–100","100")),
            Op("Set grade","pet set {id} grade {value}","Uses the species' official grade partitions; ungraded species stay ungraded.",C("value","GRADE","SSS","F","E","D","C","B","A","S","SS","SSS")),
            Op("Set bond","pet set {id} bond {value}","Sets the owned Kibo's bond level.",N("value","BOND LEVEL · 1–10","10")),
            Op("Set aptitude","pet set {id} aptitude {stat} {value}","Official aptitude levels only. IDs: 1 ATK, 3 DEF, 5 HP, 7 CRIT, 229 Mastery, 230 Break.",C("stat","APTITUDE ATTRIBUTE","1","1","3","5","7","229","230"),N("value","APTITUDE LEVEL · 1–9","9")),
            Op("Set gene","pet set {id} gene {slot} {gene}","Choose a gene by name, effect or ID. You can clear the last occupied slot.",C("slot","GENE SLOT","1","1","2","3","4","5","6","7","8"),N("gene","SELECT GENE","0")),
            Op("Set Star Soul","pet set {id} starsoul {stage} {node}","Inspect lists this species' authored Star Soul stages and nodes. Skills follow the selected node.",N("stage","STAR SOUL STAGE","1"),N("node","NODE","1")),
            Op("Add missing Kibos","pet add-missing max","Adds one Level 100 Kibo per missing configuration, with SSS grade where applicable. Already owned Kibos keep their progression.") with {NeedsSelection=false},
        ]),
        new("World","Official map difficulty settings.","world",
        [Op("Set world level","world difficulty set {map} {value}","Changing the current map's tier reloads that scene. Leave combat and campaigns first.",C("map","MAP","100","100","101"),C("value","WORLD LEVEL","1","1","2","3","4","5","6","7")) with {NeedsSelection=false}]),
    ];
}
