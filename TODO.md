# TODO

## Awaiting UAT with Ben's real library

- [ ] Drag a bubble along a user-score axis to set the score
- [ ] Filters: range sliders for numbers/dates, tick lists for categories (multi-value columns de-duped per value)
- [ ] Filters / hover / appearance shared by every plot, not saved per plot
- [ ] Game titles beside the bubbles, with collision handling
- [ ] Bubble size rescales to a filtered range instead of staying zero-anchored
- [ ] Right-click a bubble for Playnite's own game menu (borrowed, not copied)
- [ ] Numeric colour columns use a pickable colour ramp, graded like size

## Release

- [ ] Version + changelog for a first tagged release
- [ ] Package with Playnite's `toolbox.exe pack` and attach the `.pext` to the release
- [ ] Manifest PR to [PlayniteAddonDatabase](https://github.com/JosefNemec/PlayniteAddonDatabase)
- [ ] Delete the leftover `benedictcarter/playnite_charts_placeholder` repo

## Original request

> i want a "bubble plot" where the x axis is "some numerical column", the y axis "is
> another numerical column", the size is "another numerical column", the colour is
> "some categorical column", the shape is "some categorical column". the columns come
> from the playnite table. i should be able to make a series of charts (and save the
> config), hover (some column)
>
> eg date vs user score, size:critic score, colour:completion status, category:store, hover:name
>
> all the charts should be under another side tab (another tab below the statistics
> tab). there should be the "plot" to the right, and between the tabs list and the
> plot, the list of saved plots (like the library list under library)
