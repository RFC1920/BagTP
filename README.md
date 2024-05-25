# BagTP
![](https://i.imgur.com/Wvt417j.png)

Allows players to set saved teleport positions at sleeping bags they place.

This is not automatic, and the player must look at the bag and type /setbag to save.

You can also teleport to moving bags, e.g. on tugboats.

### Permissions
  - bagtp.use

### Commands
  - `/setbag` -- Used to set a teleport position at a sleeping bag.
    `/setbag HOME` -- Optional name
  - `/rembag` -- Used to remove a teleport position at a sleeping bag.

  - `/bag list` -- List saved bags
    `/bag NAME` -- Teleport to a saved bag by name.

### Configuration
```json
{
  "Options": {
    "Countdown delay for static teleport": 5.0,
    "debug": false
  },
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 3
  }
}
```


