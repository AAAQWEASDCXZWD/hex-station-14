shared-solution-container-component-on-examine-main-text = Усередині { $chemCount ->
    [1] є [color={$color}]{$desc}[/color].
   *[other] є суміш хімікатів: [color={$color}]{$desc}[/color].
}

examinable-solution-has-recognizable-chemicals = У розчині можна розпізнати: {$recognizedString}.
examinable-solution-recognized = [color={$color}]{$chemical}[/color]

examinable-solution-on-examine-volume = Розчину всередині { $fillLevel ->
    [exact] {$current}/{$max} од. [color=white]місткості[/color].
   *[other] [bold]{ -solution-vague-fill-level(fillLevel: $fillLevel) }[/bold].
}

examinable-solution-on-examine-volume-no-max = Розчину всередині { $fillLevel ->
    [exact] {$current} од. [color=white]місткості[/color].
   *[other] [bold]{ -solution-vague-fill-level(fillLevel: $fillLevel) }[/bold].
}

examinable-solution-on-examine-volume-puddle = Калюжа { $fillLevel ->
    [exact] — [color=white]{$current} од.[/color].
    [full] величезна й розтікається!
    [mostlyfull] величезна й розтікається!
    [halffull] глибока й розтікається.
    [halfempty] дуже глибока.
   *[mostlyempty] збирається в калюжку.
    [empty] розпадається на кілька маленьких калюжок.
}

-solution-vague-fill-level =
    { $fillLevel ->
        [full] [color=white]Повна[/color]
        [mostlyfull] [color=#DFDFDF]Майже повна[/color]
        [halffull] [color=#C8C8C8]Наполовину повна[/color]
        [halfempty] [color=#C8C8C8]Наполовину порожня[/color]
        [mostlyempty] [color=#A4A4A4]Майже порожня[/color]
       *[empty] [color=gray]Порожня[/color]
    }
