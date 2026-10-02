# Second PC aquarium navigation failure

User reports v1.0.21 failed with 'Open aquarium: prerequisite not detected'. Screenshot shows the Aquariums navigation label. Only the navigation area is retained as a privacy-masked fixture.

Old detector passes when the fixture is interpreted as a1032-pixel client height (confidence0.94647), but fails at1080. Adding native-pixel matching for the existing reviewed navigation templates passes both, without reducing the0.94 threshold or changing click search bounds. Exact client geometry on the remote PC remains unverified; the screenshot alone cannot prove its capture dimensions.

51 aquarium tests passed, including both capture heights and rejection after removing the Aquariums label. Local candidate1.0.21-portabilityfix.3 includes earlier aquarium close and missing-needle recovery fixes. Not live tested on the son's PC or published.
