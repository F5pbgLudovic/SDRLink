# SDRLink — WebSDR ⇄ Transceiver (English manual)
Version française : [LISEZMOI.md](LISEZMOI.md)

SDRLink displays a WebSDR in its window and keeps it locked to your rig: turn the rig's VFO and the WebSDR follows; click in the WebSDR waterfall and the rig follows. The mode follows too (LSB/USB/CW/AM/FM).

## Starting

`dist\SDRLink.exe`: a single file, which can be copied anywhere (USB stick, another PC). No installation. Runs on Windows 10 and 11.

At startup, SDRLink checks the two Windows components it needs. If one is missing, it offers to download and install it:
- **Microsoft .NET Framework 4.6.2 or later**: supplied with Windows 10 and 11. Otherwise, SDRLink offers .NET Framework 4.8.
- **Microsoft Edge WebView2 Runtime**: used to display the WebSDRs. It is present on up-to-date Windows systems; otherwise, SDRLink offers the official Microsoft installer (about 2 MB).

If the download fails, the official Microsoft page opens in the browser.

On first launch, SDRLink places `WebView2Loader.dll` in `%LOCALAPPDATA%\SDRLink\runtime\`: Windows cannot load this file directly from the exe.

To rebuild after a change: `powershell -ExecutionPolicy Bypass -File build.ps1` (output in `dist\`).

## The window, row by row

Overview; each feature is detailed further down in its own section.

1. **1st row (connection and general settings)**: **Interface** (COM port, OmniRig, TCI, HRD, Hamlib), **Brand** and **Model** (direct CAT), **Connect / Disconnect**, **Auto-connect at startup** box, **Open log** (sdrlink.log in Notepad), **Dark mode** box, **Palette** square, **Text size…**, **Check for updates** box, **Import/Export ▾**.
2. **2nd row (interface settings)**: depending on the interface, COM **Port** (with ↻ to reread the list), **Baud rate**, **Stop bits**, **CI-V address** (Icom), **RTS** / **DTR**; or **OmniRig rig**; or **Host** / **Port** (TCI, HRD, Hamlib); **TX detection (HRD)**. Then **Language** and **SDRLINK cable ▾**. With Hamlib, an extra row: **Start rigctld**, rigctld path, **Hamlib model no.**, **Download Hamlib…**.
3. **3rd row (synchronisation)**: **Sync** (Both ways, Rig → WebSDR, WebSDR → Rig, Off), **Sync mode**, **WebSDR offset (Hz)**, **Mute WebSDR while transmitting** and **restore after (ms)**, **Auto notch (DNF)**, **Scan setup**, **▶ Scan**.
4. **4th row (display and tuning)**: **PTT** button, **RIG** frequency and mode, **WEBSDR** frequency and mode, **USB, LSB, AM, FM, DIGI, CW** buttons, **kHz** entry (with history), **QSY**, **Save**, **Memories** and **▾** (last 20), wheel **Step**, **Reduced view**, **Mini view**, **Always on top**, **PTT view** boxes.
5. **5th row (WebSDR)**: **WebSDR** address (favorites list), favorites **list** shown, **Go**, **◀ Back**, **Reload**, **★ Add to favorites**, **Remove from favorites**, **Manage favorites…**, **Find WebSDRs…**.
6. **Audio row**: **⇄**, **Mute WebSDR**, **Mute TRX**, **Mute ExpertSDR**, **Mute HDSDR**, **WebSDR volume**, **TRX volume**, **ExpertSDR volume**, **HDSDR volume** (only the sounds available), then **REC**.
7. **The WebSDR page**, then the **status bar** (messages, update link).

## All right-clicks

| Where | What the right-click offers |
|---|---|
| Connect | colour when connected / disconnected |
| Open log, Text size, Scan setup, QSY, Save, Go, Back, Reload, Add / Remove / Manage favorites, Find WebSDRs, Import/Export | button colour |
| SDRLINK cable (also on left-click) | output receiving the WebSDR audio, SDRLINK name, VB-CABLE download, colour when on / off |
| Scan | colour when stopped / while scanning |
| PTT | TX safety timeout, colour when receiving / transmitting |
| RIG, WEBSDR | background colour, text colour |
| USB, LSB, AM, FM, DIGI, CW | colour of the selected mode / of the others |
| kHz entry | remove the value from the history, remove all |
| Memories | store the current frequency, button colour |
| Favorites list (next to the address) | rename the list |
| ⇄ | receivers that ⇄ keeps muted (check boxes, two at most) |
| Mute WebSDR, Mute TRX, Mute HDSDR | colour when muted / sound on |
| Mute ExpertSDR | ExpertSDR TCI port, colour when muted / sound on |
| "WebSDR volume" | ExpertSDR TCI port |
| REC | rig sound card, REC button colour, width (narrow / wide) |
| Status bar | last 20 messages, message types to hide |

## SDRLink files

- `%APPDATA%\SDRLink\settings.ini`: **all settings** (interface, favorites, memories, colours, window…). This is the file **Import/Export** copies.
- `%APPDATA%\SDRLink\settings.avant-import.ini`: the settings from before the last import.
- `%APPDATA%\SDRLink\sdrlink.log`: the log (reset at each launch).
- `%LOCALAPPDATA%\SDRLink\WebView2`: WebSDR page data (cookies, cache); `%LOCALAPPDATA%\SDRLink\runtime`: WebView2Loader.dll.
- `Music\SDRLink`: default REC recordings folder; while recording, a temporary file `SDRLink-REC-*.raw` is in `%TEMP%`.

## Interfaces ("Interface" menu)

| Interface | Settings | Notes |
|---|---|---|
| COM port (direct CAT) | Brand, model, port, baud rate, stop bits, RTS/DTR, CI-V address (Icom) | The port must not be open in another program |
| OmniRig | Rig 1 / Rig 2 | The rig is configured in OmniRig; lets you share the rig with other programs. **If OmniRig does not work, use OmniRig version 1, run as administrator** (see Troubleshooting) |
| TCI | Host / port (40001 by default) | ExpertSDR (SunSDR), Thetis… enable the TCI server |
| Ham Radio Deluxe | Host / port (7809 by default) | Enable the IP server in HRD Rig Control |
| Hamlib (rigctld) | Host / port (4532 by default), "Start rigctld", Hamlib model no. | See below |

## Hamlib

Hamlib is the free rig-control library used by WSJT-X, fldigi, JTDX… It supports more than 300 models. SDRLink talks to **rigctld**, Hamlib's small network server.
- **Start rigctld** (ticked by default): SDRLink starts rigctld itself on connection and stops it on disconnection. It uses the rig, COM port, baud rate and stop bits (and RTS/DTR if ticked) from the settings row, which is then shown.
  - The location of `rigctld.exe` is found automatically (Hamlib installation, PATH, then WSJT-X/JTDX). Otherwise, the **…** button lets you point to it.
  - **Download Hamlib…** opens the official page (github.com/Hamlib/Hamlib/releases). Take `hamlib-w64-….exe` (installer) or the `.zip`.
  - The `rigctld-wsjtx.exe` supplied with WSJT-X or WSJT-Z does not always start on its own, outside WSJT: in that case, install Hamlib.
- **Hamlib model no.**: filled in from the chosen model (FT-2000 = 1029, FTdx10 = 1042, IC-7000 = 3060…). For another rig, type the number from the Hamlib list (`rigctl -l`).
- **Unticked**: SDRLink connects to an already running rigctld, at the **Host / Port** address given (even on another PC).
- A COM port can only be used by one program: do not start rigctld on a port already opened by HRD or OmniRig.
- Functions: frequency, mode (DIGI mode goes through PKTUSB/PKTLSB, on Icoms too), PTT, transmit status, volume (Mute TRX, TRX volume) if the rig allows it.
- In case of error (COM port busy, unknown model…), the rigctld message is shown in the status bar and in the log.

## Rigs in direct CAT

In the **Model** list, rigs are sorted by number (FT-100, FT-450… FT-2000, then FTdx…, FTX-1). The list was compared with those of Hamlib, OmniRig and Ham Radio Deluxe; the protocols come from Hamlib.

- **Yaesu** (40):
  - current protocol (text commands): FT-450/450D, FT-710, FT-891, FT-950, FT-991/991A, FT-2000, FTdx10, FTdx101D/MP, FTdx1200, FTdx3000, FTdx5000, FTdx9000, FTX-1;
  - old binary protocol: FT-100/100D, FT-600, FT-650, FT-747GX, FT-757GXII, FT-767GX, FT-817, FT-817ND, FT-818, FT-818ND, FT-840, FT-847, FT-857, FT-857D, FT-890, FT-897, FT-897D, FT-900, FT-920, FT-990, FT-1000, FT-1000D, FT-1000MP, FT-1000MP Mark-V, FT-1000MP Mark-V Field;
  - command only (the rig sends nothing back): FT-757GX, FT-736R.
- **Icom** (55): IC-78, IC-271, IC-275, IC-375, IC-471, IC-475, IC-575, IC-703, IC-705, IC-706, IC-706MKII, IC-706MKIIG, IC-707, IC-718, IC-725, IC-726, IC-728, IC-729, IC-735, IC-736, IC-737, IC-738, IC-746, IC-746PRO, IC-751/751A, IC-756, IC-756PRO, IC-756PROII, IC-756PROIII, IC-761, IC-765, IC-775, IC-781, IC-820H, IC-821H, IC-905, IC-910H, IC-970, IC-1275, IC-7000, IC-7100, IC-7200, IC-7300, IC-7300MK2, IC-7400, IC-7410, IC-7600, IC-7610, IC-7700, IC-7760, IC-7800, IC-7850, IC-7851, IC-9100, IC-9700
- **Kenwood** (23): TS-50S, TS-60S, TS-140S, TS-440S, TS-450S, TS-480, TS-570, TS-590S/SG, TS-680S, TS-690S, TS-711, TS-790, TS-811, TS-850S, TS-870S, TS-890S, TS-930S, TS-940S, TS-950S, TS-950SDX, TS-990S, TS-2000, TS-B2000
- **Elecraft** (5): K2, K3/K3S, K4, KX2, KX3
- **Xiegu** (5, Icom-compatible CI-V): G90, X108G, X5105, X6100, X6200

Not in the list: the **FT-850 and FT-870 do not exist at Yaesu** (they are the Kenwood **TS-850S** and **TS-870S**, which are in the Kenwood list; Yaesu made the FT-840, FT-890 and FT-900). The **FT-980** is not available in direct CAT (its protocol is only a test version in Hamlib): use the Hamlib interface (no. 1031) or HRD. Receivers (IC-R…, FRG…, R-5000) and FM handhelds are not offered.

When you choose a model, the baud rate, stop bits and CI-V address are set to the factory values (Hamlib's). They must match the rig's menu:

- **FT-2000**: CAT RATE menu (4800 by default). Tested on F5PBG's station: 38400, 1 stop bit, RTS unticked.
- **FTdx10**: CAT RATE menu; over USB, use the "Enhanced" COM port. If nothing answers, try RTS ticked/unticked.
- **FTdx9000**: 4800 baud, 2 stop bits by default. **FTX-1**: 38400 baud, 1 stop bit by default (set them as in the rig's CAT menu).
- **IC-7000**: CI-V address 70 (hex), CI-V baud rate from the menu (often 19200 or Auto). CI-V Transceive can stay ON.
- **FT-817/818, FT-857/857D, FT-897/897D**: Yaesu's old binary CAT protocol, 2 stop bits, baud rate as set in the rig's CAT RATE menu (4800 by default). The rig volume (Mute TRX, TRX volume) cannot be controlled with this protocol.
- **FT-847, FT-650**: same commands as the FT-817; SDRLink puts the rig in CAT mode when connecting and takes it out when disconnecting. Suggested baud rate: 57600 for the FT-847 (CAT RATE menu), 4800 for the FT-650. The FT-847 has no data mode: a data mode on the WebSDR puts it in USB or LSB. Only FT-847s built from the 8G05 series on can report their frequency (according to Hamlib).
- **FT-990, FT-1000, FT-1000D, FT-1000MP, Mark-V**: old binary CAT protocol (the same as Hamlib), 4800 baud, 2 stop bits, no baud rate setting on the rig. Frequency in 10 Hz steps. No PKT on the USB side: a USB data mode on the WebSDR puts the rig in USB. The rig volume (Mute TRX, TRX volume) cannot be controlled. The FT-1000MP and Mark-V do not reliably report over CAT whether they are transmitting: in direct CAT, automatic muting while transmitting therefore does not work with them (the FT-990 and FT-1000/1000D do report it). The FT-990 is slow to receive commands (50 ms per byte, as in Hamlib), so VFO tracking is a little slower. Through OmniRig or HRD, choose the rig in those programs; through Hamlib, the model number is filled in automatically (FT-990: 1016; if your FT-990 has an old ROM and does not answer, try 1048).
- **FT-100/100D, FT-600, FT-747GX, FT-757GXII, FT-840, FT-890, FT-900, FT-920**: old binary CAT protocol, 4800 baud, 2 stop bits (no baud rate setting on these rigs). SDRLink reads the rig's status block, as Hamlib does. The rig volume cannot be controlled. Specifics:
  - **FT-747GX**: the rig sends back a long block (344 bytes), so one reading takes almost 1 second; it does not answer while transmitting ("The rig does not answer" may appear during a long transmission, then everything resumes);
  - **FT-757GXII**: no PTT command over CAT (on this rig, transmit is keyed by a pin of the CAT socket): SDRLink's PTT button has no effect, but transmitting is detected;
  - **FT-600**: the transmit state cannot be read (no automatic muting while transmitting), no FM;
  - **FT-920**: DATA-L / DATA-U data modes are recognised.
- **FT-767GX**: special protocol (the rig echoes each command, which is then acknowledged), 4800 baud, 2 stop bits. SDRLink puts the rig in CAT mode for each reading, then releases it: the front panel is locked for those few tenths of a second. No PTT command over CAT; transmitting is detected.
- **FT-757GX** (the first one, without "II") and **FT-736R**: these rigs receive commands but send nothing back. SDRLink therefore cannot read their frequency: it shows the last frequency it sent, and only the **WebSDR → Rig** direction makes sense (choose it in **Sync**). The FT-736R is put in CAT mode when connecting; it accepts PTT, not AM; above 1.2 GHz the frequency is encoded as in Hamlib. The FT-757GX has no PTT over CAT.
- **Older Kenwoods** (TS-50S, TS-60S, TS-140S, TS-440S, TS-450S, TS-680S, TS-690S, TS-711, TS-790, TS-811, TS-930S, TS-940S, TS-950S/SDX): 4800 baud, 2 stop bits (IF-232C interface or equivalent). They do not answer the `MD;` mode query: SDRLink notices this at the first reading and then reads the mode from the `IF;` answer (the log says so). The TS-140S, TS-680S, TS-711 and TS-811 do not report over CAT whether they are transmitting (no automatic muting while transmitting). **TS-850S**: 4800 baud, 2 stop bits. **TS-B2000**: same as the TS-2000.
- **Icom**: each model has its factory CI-V address (IC-7760: B2, IC-706: 48, IC-706MKII: 4E, IC-7300MK2: B6, IC-905: AC…); if the rig was set differently, type its address in **CI-V address**. The 1980s Icoms (IC-271, 275, 375, 471, 475, 575, 725, 726, 728, 729, 735, 751, 761, 765, 781, 1275) are offered at **1200 baud**, their original CI-V speed. The **IC-735** uses a 4-byte frequency (handled automatically). On older Icoms, transmit-state reading and volume are not available: SDRLink detects this and then hides TRX volume and Mute TRX.
- **Xiegu**: the CI-V address depends on the firmware. If the rig does not answer, try another address:
  - G90: 88 (factory value according to Hamlib and the Radioddity manual), otherwise 70;
  - X108G, X5105: 70;
  - X6100: A4, otherwise 70 for firmware older than 1.1.8;
  - X6200: A4.
  Set the baud rate as in the rig's CI-V menu.

These new models (version 1.14) were written from Hamlib and checked against simulated data blocks, not on real rigs: if something goes wrong, the log (**Open log**) helps to understand, and the **Hamlib** interface remains a fallback.

## Compatible WebSDRs

The type is detected automatically (shown in brackets):
- **websdr.org** (PA3FWM), e.g. Twente
- **UberSDR** (v2 interface), e.g. https://wessex.zapto.org/v2/
- **PhantomSDR** (with the CATsync module), e.g. http://linkz.ddns.net:8088/ (French Alps WebSDR)
- **KiwiSDR**
- **OpenWebRX / OpenWebRX+** (only within the range of the band displayed)

## Favorites

- The drop-down list in the WebSDR bar shows the favorites by **name**. Choosing one opens the WebSDR.
- **Favorite lists**: from 1 to **10 lists** (4 at the start: List 1 to List 4) to sort the WebSDRs.
  - The menu just to the right of the WebSDR list chooses the list shown.
  - **Right-click** this menu to rename the list (e.g. "HF France", "VHF"…).
  - Each time you add one (★, manager, search), SDRLink **asks for the list** to save to (the list shown is suggested). Imported favorites go into their own list (see Import). Existing favorites are in list 1.
  - In "Manage favorites…":
    - the **List** menu filters the display (or "All lists"); right-click it to rename the list;
    - **New list** adds a list at the end (10 at most) and asks for its name;
    - **Delete list…** deletes the list shown **and its favorites**, after confirmation (at least one list always remains). The following lists move up one place; a list that was never renamed takes the name of its new position ("List 5" becomes "List 4");
    - **Move to ▸** moves the ticked favorites (or, failing that, the selected favorite) to another list;
    - a "List" column shows the list of each favorite.
- **★ Add to favorites**: adds the page shown, named after the page title (editable), to the list chosen in the window. If it is already in that list, it is not added a second time (a message says so). The same page can be in several lists.
- **Remove from favorites**: removes the page shown from the **list shown only**, after confirmation. It stays in the other lists.
- **Manage favorites…** opens the manager:
  - **Add favorite…**;
  - a **tick box** in front of each favorite. **Remove…** removes all the ticked favorites at once, or the selected favorite if none is ticked.
  - **Rename…**, **Move up**, **Move down** for the selected favorite;
  - **Check favorites**:
    - queries each address (10 at a time, 8 s maximum each) and shows the result in the Status column;
    - **duplicates** (same address in the same list) are **removed automatically**: only the first is kept;
    - **dead links** (no answer, unknown host, connection refused, page not found 404) are **ticked automatically** and shown in red;
    - all that remains is to click "Remove…". A server that answers, even with an access refusal, is considered alive.
  - **Check all** / **Uncheck all** (favorites shown);
  - **Name** and **Address** fields: they show the selected favorite and can be edited directly. The list updates as you type. A field left empty is not applied.
  - **Listen to selection**: opens the selected favorite in SDRLink to listen to it, without closing the manager (as in the WebSDR search).
  - **Open** or double-click: opens the WebSDR and closes the manager. The Delete key removes, Enter opens.
  - **Export…**: **all** favorites, from all lists:
    - **HTML** (offered first): bookmarks file in **Firefox** format, which can also be imported into Edge and Chrome. It contains an **SDRLink** folder with one sub-folder per list, named after the list (all lists, even empty ones). The volume of each WebSDR is kept there for SDRLink; browsers ignore it.
    - **CSV** (readable by Excel): `nom;adresse;volume;liste;numéro de liste` (name; address; volume; list; list number).
    - In Firefox: Bookmarks > Manage Bookmarks (Ctrl+Shift+O) > Import and Backup > **Import Bookmarks from HTML**. The SDRLink folder appears in the bookmarks menu.
  - **Import…**: each favorite goes **into its own list**:
    - file exported by SDRLink, or exported by Firefox (Import and Backup > **Export Bookmarks to HTML**): only the contents of the **SDRLink** folder are imported, wherever it is in the bookmarks (toolbar, menu…). Each sub-folder goes into the list with the same name, otherwise into the list at the same position (1st sub-folder → list 1…), created if needed (up to 10 lists). A list that was never renamed takes the name of its sub-folder.
    - bookmarks from a browser without an SDRLink folder: a folder named after a list goes into that list; the rest goes into the list shown in the manager.
    - CSV file `nom;adresse;volume;liste;numéro` (name; address; volume; list; number), or `nom;adresse` (name; address), or a plain list of addresses: the list with the same name, otherwise the one with that number (created if needed); an unknown list name without a number creates a new list if there is room left; without a list column, the list shown.
    - a WebSDR already in its list is not added a second time. The final message gives the number imported per list.
    - the volume is taken from an SDRLink export (HTML or CSV); Firefox does not keep it.
- The default favorites include the websdr.org, KiwiSDR and OpenWebRX directories, as well as Twente, Wessex and French Alps.

## Finding WebSDRs

The **Find WebSDRs…** button (WebSDR bar) queries three public directories:
- **websdr.org**;
- the official **KiwiSDR** list;
- **ReceiverBook** (OpenWebRX, WebSDR, KiwiSDR).

Duplicates are removed. The list is shown **sorted by distance from the centre of France**.
- **Max distance** (1000 km by default): France, Belgium, Luxembourg, Switzerland, northern Italy and Spain, western Germany, the Netherlands, southern England… Increase or reduce as you like.
- **Filter**: free text (town, callsign, département…). **Type**: websdr.org, KiwiSDR, OpenWebRX…
- Receivers **already in favorites** (in any list) are not offered; the bottom of the window shows how many are hidden this way. KiwiSDR proxies (xxx.proxy.kiwisdr.com) with or without :8073 count as a single receiver.
- Click a column heading to sort by that column.
- **Country**: estimated from the name (country mentioned, French callsign F…/TK…) or, for France, from the GPS position. Empty if unknown.
- **Listen to selection** (or double-click): opens the receiver in SDRLink to try it.
- Tick the ones you are interested in, then **Add checked to favorites**: SDRLink asks which list to save them to. They can then be renamed in "Manage favorites…".

**First start**: the search opens by itself, once only, if the Internet answers, to fill in the favorites. Without a connection, it is tried again at the next start.

Internet access is required. If a directory does not answer, the others are still shown: the bottom of the window shows the result for each source.

## WebSDR volume

The audio row, always at the start of the row below the WebSDR bar, reads from left to right: the ⇄ button, Mute WebSDR, Mute TRX, Mute ExpertSDR, Mute HDSDR, then WebSDR volume, TRX volume, ExpertSDR volume and HDSDR volume, each with its slider and its value in %. Mute TRX and TRX volume only appear if the rig volume is remotely controlled, Mute ExpertSDR and ExpertSDR volume only if ExpertSDR is running and answers (Windows audio or TCI), Mute HDSDR and HDSDR volume only if HDSDR is running, and ⇄ only if there are at least two audio sources to switch between. Missing controls leave no gap: the row closes up to the left. The **WebSDR volume** slider only sets the WebSDR audio, not that of Windows or other programs.
- It works on all WebSDR types. On UberSDR, the audio only starts after clicking "Click to start" on the page.
- The volume is **remembered for each favorite**: when you reopen a favorite, you get its volume back.
- WebSDRs that are not in favorites share a common volume, which is also remembered.
- An added favorite keeps the current volume. Export (HTML or CSV) includes the volume, and import restores it.

## Muting and rig volume

To the right of the WebSDR volume:
- **⇄** (small square button): switches the audio from one receiver to another among **Mute WebSDR**, **Mute TRX**, **Mute ExpertSDR** and **Mute HDSDR** (only those available: Mute TRX if the rig volume is remotely controlled, Mute ExpertSDR if ExpertSDR answers, Mute HDSDR if HDSDR plays through Windows audio):
  - nothing muted: one click mutes them all;
  - only one source active: it is muted and the next one is restored; each click moves to the next (WebSDR → TRX → ExpertSDR → HDSDR → WebSDR…);
  - everything muted: the first is restored; several sources active (but not all): all muted.
  - **Right-click** ⇄: tick the mutes that ⇄ ignores, **two at most** (or "None" to untick them all). A ticked mute is muted at once and stays muted; ⇄ then only switches between the remaining receivers. With four receivers, ticking two lets you switch between the other two only. When two are ticked, the others are greyed out: untick one to choose another. The choice is kept.
- Mute button colours: **green with white text** when the audio is passing (like the selected mode button), **red with white text** when it is muted, grey when muting is not available.
- **Mute WebSDR**: mutes the WebSDR audio (red button); a second click restores it at the set volume.
- **Mute TRX**: mutes the **transceiver** audio by setting its AF gain to zero over CAT, then restores the volume on the second click. The audio is also restored on disconnection.
- **Mute ExpertSDR**: only appears when the **ExpertSDR** software (2 or 3) is running and answers. It mutes and restores ExpertSDR's audio without going into ExpertSDR:
  - through the **Windows volume mixer** if ExpertSDR plays through Windows audio;
  - otherwise (audio coming out of the rig itself, MB1, or through ASIO) through the **TCI** `mute` command: you must then enable the TCI server in ExpertSDR (Options > TCI). SDRLink keeps a TCI link with ExpertSDR as long as it is running, even if the rig is connected some other way (HRD…): a mute or a volume set in ExpertSDR is also shown in SDRLink. A message explains this if the TCI server does not answer.
  - **Right-click** Mute ExpertSDR, or "WebSDR volume": ExpertSDR TCI server port (**40001** by default), to be set as in ExpertSDR. As long as ExpertSDR does not answer, Mute ExpertSDR and ExpertSDR volume are hidden and the status bar says to use "WebSDR volume".
  - If SDRLink muted ExpertSDR, the audio is restored when SDRLink closes.
- **Mute HDSDR**: only appears when the **HDSDR** software is running (on the same PC), whether it is connected to the rig through HRD, OmniRig or otherwise. It mutes and restores HDSDR's audio in the **Windows volume mixer**, without touching HDSDR or its CAT link, and without any extra COM port. ExpertSDR and HDSDR can run at the same time: each has its own Mute and volume.
  - HDSDR must play through Windows audio (MME or WDM output, the usual setting). If its reception is stopped or its audio goes out through ASIO, Mute HDSDR stays grey, HDSDR volume shows "—" and clicking Mute HDSDR explains this in the status bar.
  - A mute or a volume set in the Windows mixer is also shown in SDRLink (read again every 3 seconds).
  - If SDRLink muted HDSDR, the audio is restored when SDRLink closes.
  - **Right-click** Mute HDSDR: colour when muted / sound on.
- **TRX volume**: sets the transceiver volume (AF gain, in %). The slider also follows the rig's knob: it is read back about every 2 seconds.
- **TRX volume** and **Mute TRX** are only shown if the rig volume is remotely controlled (rig connected, and an interface that allows it).
- **ExpertSDR volume** (to the right of TRX volume): ExpertSDR volume, through the Windows mixer or through TCI (`volume:` from −60 to 0 dB), like Mute ExpertSDR. Shown when ExpertSDR is running; greyed out as long as neither path answers.
- **HDSDR volume** (to the right of ExpertSDR volume): HDSDR volume in the Windows mixer (0 to 100 %). Shown as soon as HDSDR is running, it disappears when HDSDR is closed; greyed out as long as HDSDR does not play through Windows audio.

Commands used (checked against the Yaesu CAT manual and Hamlib):
- **Yaesu**: `AG0nnn;`, from 000 to 255.
- **Kenwood**: `AGnnn;` or `AG0nnn;` depending on the model, detected automatically. From 000 to 255.
- **Elecraft**: `AGnnn;`, from 000 to 250 (K3, KX3, KX2) or from 000 to 060 (K4).
- **Icom / Xiegu**: CI-V `14 01`, value from 0000 to 0255.
- **TCI**: `volume:` from −60 to 0 dB.
- **HRD**: "AF gain (main)" slider in HRD Rig Control (otherwise "AF gain" or "Volume"), via `get/set slider-pos`. It is looked up on connection, and again when you change rig in HRD (FT-2000, FTdx10, FTdx5000…) without reconnecting SDRLink; the log shows this with the line "HRD curseur volume …". If HRD has no volume slider for the rig, TRX volume and Mute TRX are hidden and the log lists the available sliders.
- **Not available** with Yaesus using the old binary protocol (FT-100, FT-600, FT-650, FT-736R, FT-747GX, FT-757GX/GXII, FT-767GX, FT-817/818, FT-840, FT-847, FT-857, FT-890, FT-897, FT-900, FT-920, FT-990, FT-1000/1000D/1000MP: no command), with older Icoms and Kenwoods that do not know the command (detected automatically) and with OmniRig (no volume control): TRX volume and Mute TRX are then hidden.

## Dark mode

The **Dark mode** box, to the right of "Open log", switches the whole interface to dark colours: window, title bar (Windows 10 20H1 and later, Windows 11), lists, buttons and favorite manager. The WebSDR page keeps its own colours.
- Frequencies, PTT and mode buttons take lighter shades, readable in low light.
- Settings greyed out while connected remain readable.
- **Palette**: the small multicoloured square to the right of "Dark mode" opens a choice of background colours.
  - In dark mode: dark shades only (black, graphite, night blues, purple, burgundy, very dark red, brown, dark green and teal).
  - In light mode: light shades only (white, pearl grey, cream, sand, pale yellow, blues, aqua green, pink, lavender).
  - The rest of the interface (fields, buttons, borders) adjusts automatically. "Default" returns to the original colours.
  - The choice is remembered separately for dark mode and light mode.

## Button colours (right-click)

Right-clicking most buttons offers their **background colour**. If the button already had a right-click menu (PTT, Memories, SDRLINK cable, Mute ExpertSDR…), that menu is kept and the colours are added at the end.
- **One choice** ("Button colour"): Open log, Text size, Scan setup, QSY, Save, Memories, Go, Back, Reload, Add to favorites, Remove from favorites, Manage favorites, Find WebSDRs, Import/Export.
- **Two choices, one for each state**:
  - **Connect**: when connected / when disconnected;
  - **SDRLINK cable**: when on / when off;
  - **Scan**: scan stopped / while scanning;
  - **PTT**: when receiving / when transmitting (PTT on);
  - **RIG** and **WEBSDR** (frequency and mode): background colour / text colour (when transmitting, RIG always turns red);
  - **USB, LSB, AM, FM, DIGI, CW**: selected mode / other modes (the choice applies to all six buttons);
  - **Mute WebSDR**, **Mute TRX**, **Mute ExpertSDR**, **Mute HDSDR** (each its own): muted / sound on. An unavailable Mute stays greyed out.
- Colours offered: **Default** (the original colour, which follows dark mode and the palette), navy blue, blue, sky blue, teal, green, red, burgundy, purple, pink, brown, grey, black, yellow, orange, white, or any colour with **Other colour…**.
- The text turns black on light colours and white on dark ones; a border outlines the button.
- The choices are kept, including when switching between light and dark mode.
- The REC button keeps its own menu (colour and width, see "Recording (REC)").

## Language

The **Language** menu, on the 2nd row, changes the language of the whole interface, immediately and without restarting. The choice is kept.
- Languages: French, English, German, Spanish, Italian, Dutch, Portuguese, Polish, Czech, Slovak, Slovenian, Croatian, Hungarian, Romanian, Bulgarian, Greek, Swedish, Danish, Norwegian, Finnish, Estonian, Latvian and Lithuanian.
- Languages other than French and English are translated from English. Any missing text is shown in English.
- The diagnostic log stays in French.
- To correct a translation: file `lang\<code>.txt` (English text, tab, translation), then `tools\langgen.ps1` and rebuild.

## Synchronisation

- **Both ways** / **Rig → WebSDR** / **WebSDR → Rig** / **Off**
- When you choose **Both ways** or **Rig → WebSDR**, the WebSDR is immediately re-tuned to the rig's frequency.
- When you choose **WebSDR → Rig**, it is the rig that immediately tunes to the WebSDR's frequency and mode.
- **When SDRLink starts, the rig never changes** frequency or mode: in Both ways or Rig → WebSDR, it is the WebSDR that tunes to the rig; in WebSDR → Rig or Off, nothing changes. Likewise, for the 8 seconds after a page loads or the rig connects, the WebSDR does not control the rig: a page that is still settling sometimes reports its old frequency, which used to be sent back to the rig.
- In **Both ways**, the **rig is the reference**: if the WebSDR stays off frequency for more than 3 seconds, it is re-tuned to the rig. This happens if a setting is lost or if the page is slow. In "Rig → WebSDR", on the other hand, you can move the WebSDR away from the rig (mouse wheel or click in the waterfall): it is only re-tuned at the next VFO movement.
- **OpenWebRX with several profiles** (one profile per band, e.g. "HF_7MHz", "VHF_144-146MHz"): if the requested frequency is outside the active profile, SDRLink automatically switches to the profile that covers it, based on its name, then sets the frequency. The profile change applies to all listeners of that receiver, just as when done by hand.
- **WebSDR offset (Hz)**: added to the rig frequency on the WebSDR side (transverter, calibration error…)
- **◀ Back** (WebSDR bar): returns to the previous page, like a browser's back arrow. Useful after following a link, for example in a WebSDR directory.
- If the rig is in DATA-U and the WebSDR in USB, SDRLink does not change the rig's mode (same family).

## PTT button

The large **PTT** button, to the left of "RIG", puts the rig into transmit over CAT: one click to transmit (the button turns red, "● TX"), a second click to return to receive. It is the equivalent of HRD's MOX button.
- It sends `TX1;`/`TX0;` to Yaesus, `TX;`/`RX;` to Kenwoods and Elecrafts, `1C 00` to Icoms, `trx` over TCI.
- With OmniRig, it acts on the `Tx` property; with HRD, on the MOX button.
- The WebSDR audio is muted as soon as you click, even if the rig does not report its transmit status (if "Mute WebSDR while transmitting" is ticked).
- For safety, if you disconnect or close SDRLink during a transmission started with this button, the rig goes back to receive.
- **TX safety timeout** (right-click PTT): Off, 30 s, 1, 2, 3, 5, 10, 15, 20 or 25 min.
  - While transmitting, the button shows the time remaining ("● TX 1:45").
  - When it expires, SDRLink returns to receive and says so at the bottom of the window.
  - This timeout applies to transmissions started with SDRLink's PTT button, not the footswitch or the microphone.
- **Transmitting without modulation** (seen on an FTdx10): CAT PTT does put the rig into transmit, but the transmitted audio depends on the rig's **modulation source**. If it is set to the rear connector or USB ("REAR", often the case for FT8), the microphone is not transmitted. On the FTdx10, check in the transmit settings menus (TX AUDIO) the SSB modulation source (MOD SOURCE: MIC) and the mic gain. Other programs that key the rig over CAT, such as WSJT-X, behave in the same way.

## Muting while transmitting

Tick **"Mute WebSDR while transmitting"** (on by default): while the rig is transmitting, the WebSDR audio is muted so you do not hear yourself. The **● TX** indicator is then shown in red next to "RIG". Untick it to keep the audio while transmitting.

**"restore after (ms)"** (1000 by default): WebSDR audio arrives 0.5 to 2 s late, so it still contains the end of your transmission after you release the PTT. Increase this value if you hear the end of your overs; set 0 to restore the audio immediately.

Reading the transmit status, depending on the interface:
- Yaesu: `TX;` command
- Kenwood/Elecraft: `IF;` command
- Icom: CI-V command `1C 00`
- OmniRig: `Tx` property
- TCI: `trx` message
- HRD: `MOX`, `TX`, `PTT` buttons (and `VOX`, see below)

If the rig does not provide this information, the log says so and the audio is never muted.

**With HRD, the footswitch or microphone PTT is not always seen.** HRD's IP server has no "transmit status" command. With the FT-2000, the MOX button only changes if you click it in HRD. With the footswitch, only the VOX button changes state. The **"TX detection (HRD)"** menu offers two choices:
- **MOX/TX/PTT buttons**: transmit only if one of these buttons is pressed.
- **MOX/VOX/TX/PTT buttons**: in addition, SDRLink reads the value of the VOX button on connection, then treats any other value as transmitting.
  - Connect while the rig is in receive.
  - HRD takes about 1 s to update this button.
  - Avoid this if you actually use the rig's VOX.

Changing the choice automatically reconnects to HRD.

**Fastest solution if the rig is shared through virtual ports** (com0com + hub4com, HRD on one port): connect SDRLink as **COM port (direct CAT)** on another virtual port of the share, with HRD still open. SDRLink then queries the rig directly (`TX;` command), without going through slow HRD.

## Mode buttons

The **USB, LSB, AM, FM, DIGI, CW** buttons, to the right of the WEBSDR frequency, change the mode of the rig **and** the WebSDR at the same time. The button of the current mode is green.
- **DIGI**: the rig's data mode (PKT, DATA, USB-D… depending on the brand), USB or LSB on the WebSDR side. Clicking DIGI again while it is lit toggles between **DIGI U** (DATA-USB) and **DIGI L** (DATA-LSB).
- **CW**: clicking CW again while it is lit toggles between **CW U** (normal CW) and **CW L** (CW-R). WebSDRs have only one CW mode. Over TCI, there is only one CW mode.
- Icom in direct CAT: DATA mode cannot be controlled through this interface; DIGI puts the rig in USB or LSB.

## SDRLINK cable (WebSDR audio to WSJT-X…)

The WebSDR audio is copied **all the time, in every mode**, to a virtual audio cable. The other end of the cable, named **SDRLINK**, appears in the list of **audio inputs** of WSJT-X, JTDX, fldigi…
- The copy is not affected by **Mute WebSDR**, volume at 0, muting while transmitting or the notch: you can decode without hearing the WebSDR.
- **A virtual cable driver is required.** SDRLink cannot create a cable itself: Windows requires a signed driver for that. Install **VB-CABLE** from VB-Audio, free: **SDRLINK cable ▾** button › "Download VB-CABLE".
  - If VB-CABLE is already used for something else, VB-Audio also offers additional cables ("VB-CABLE A+B", "C+D").
- **SDRLINK cable ▾ button** (2nd row):
  - choice of the output that receives the audio (the "playback" end of the cable, e.g. "CABLE Input"), or **Off**;
  - **Name the other end of the cable "SDRLINK"**: renames the "recording" end ("CABLE Output") to `SDRLINK`, as the Windows Sound panel does. Windows asks for administrator permission. Then restart WSJT-X and choose the **SDRLINK** input.
- Automatic choice: the cable whose other end is called SDRLINK, otherwise a VB-Audio cable that has kept its original name (CABLE Input, CABLE-A Input…). A cable already renamed, the speakers or a radio's sound card are **never** picked automatically. Choosing them by hand requires confirmation.
- Never choose a radio's **transmit** sound card (e.g. "Emission FT2000"): the WebSDR audio would be transmitted.
- If the status bar says "output already used in exclusive mode", another program has reserved the cable. In the Sound panel, open the cable's properties, Advanced tab, and untick "Allow applications to take exclusive control of this device". Otherwise, close that program.

## Automatic notch (DNF)

**Auto notch (DNF)** box, at the end of the Sync row: like the FT-2000's DNF, it automatically removes **steady carriers and whistles** from the WebSDR audio.
- Up to 4 tones at a time. Each tone is detected in about 0.6 s, then tracked if it drifts. The measured attenuation is about 48 dB, down to the noise level.
- Speech moves too much to be caught. A steady **CW** signal, however, is removed, as with the rig's DNF: untick it for CW.
- Only the audio you hear is filtered; the SDRLINK cable receives the raw audio (decoders need it).
- The choice is remembered.

The buttons always keep the same place: the RIG and WEBSDR areas have a fixed width. The WebSDR mode is shown there in short form (USB, LSB, CW, AM, FM). Transmitting is shown by the RIG frequency in red and by the PTT button. FM becomes NBFM on KiwiSDR and NFM on UberSDR and OpenWebRX.

## Entering a frequency

- Type a frequency in **kHz** in the "kHz" field (e.g. `7074`, `14074,5` or `14074.5`), then **Enter** or **QSY**: the rig and the WebSDR tune to it.
- The drop-down menu keeps the **last 100** frequencies, the most recent at the top. Choosing a value from the list tunes to it directly.
- **Save** adds the current frequency to the list.
- **Right-click** the field: "Remove "…" from the list" (the value shown) or "Remove all" (confirmation is requested).
- The list is kept for the next launch.

## Mouse wheel on the frequencies

Hover the mouse over **RIG** or **WEBSDR** and turn the wheel. What moves depends on the synchronisation:

| Sync | Wheel over RIG | Wheel over WEBSDR |
|---|---|---|
| Both ways | rig and WebSDR | rig and WebSDR |
| Rig → WebSDR | rig and WebSDR | WebSDR only |
| WebSDR → Rig | rig only | rig and WebSDR |
| Off | rig only | WebSDR only |

- The **step** is chosen in the "Step" menu on the same row: 100 Hz, 250 Hz, 500 Hz, 1 kHz, 2.5 kHz, 5 kHz or 10 kHz.
- The frequency is rounded to the step: with a 1 kHz step, you land on whole kHz.
- **Acceleration** if the step is less than 1 kHz, depending on the time between two notches:
  - less than 100 ms: step × 2;
  - less than 60 ms: step × 5;
  - less than 30 ms: step × 10.

## Display

- **Reduced view**: the window starts at the "Sync" row and keeps **everything below**, including the WebSDR page. The connection and interface settings rows are hidden; the window loses their height, the page keeps its size. The window can be resized.
- **Mini view** (formerly "Reduced view"): the window only keeps the rows from "Sync" to "WebSDR volume" (PTT, frequencies, modes, WebSDR bar, volumes), at a fixed height. The WebSDR page is hidden, but the WebSDR keeps playing and following the rig.
- In reduced and mini views, the PTT / frequencies / modes / boxes row stays **on a single line**: the window cannot become narrower than it (unless the screen is too small), only the height can be adjusted. The window is also kept entirely on screen (bottom visible, above the taskbar); when maximised, reduced view stays maximised (the whole WebSDR page is visible), and so does the return to normal view; mini view, however, is always a small window.
- Only one of the two boxes at a time. **Unticking** the ticked box always returns to normal view (both boxes unticked), even if you had switched from reduced to mini or from mini to reduced.
- Each view gets back the size it had when you left it (normal and reduced), including from one session to the next: sizes are saved on closing, together with the current view. If the window was maximised in normal view, it returns to that.
- **PTT view** (below "Always on top"): a very small window that only shows the **PTT button** and the "PTT view" box. PTT works normally there (click, right-click for the safety timeout). **Unticking** the box returns to the previous view **exactly as it was**: same view (normal, reduced or mini), same size, same position, maximised state included. Closing SDRLink in PTT view saves the previous view.
- **Always on top**: SDRLink stays above other windows (handy with PTT view).
- **Text size…** (1st row): enlarges or reduces the text of the main window, from 70 to 200% (100% = original size), with the slider or the − / + buttons. The change is visible at once; **Cancel** returns to the previous size, **100 %** to the original size. Fixed-size buttons and sliders follow. The size is kept for the next launch.

These two settings are kept for the next launch.

## Scan

On the "Sync" row, after "Auto notch (DNF)":
- **Scan setup**: choice of scan type.
  - **Frequencies**: **start** and **end** frequency in kHz (in any order), **step** (100 Hz to 100 kHz), **time** on each frequency: 1 s, 2 s, 5 s, 10 s, 30 s, 1 min, 2 min, 5 min or 10 min. When it reaches the end, the scan starts again from the beginning.
  - **Memories**: list of memories with a **tick box** for each (Check all / Uncheck all), and the **time** on each memory (same choices). The stored mode is applied.
  - Each type keeps its own settings: when switching from Memories back to Frequencies (or the other way round), the previous choices are still there. **Reset** clears the type shown (the other keeps its own).
  - **OK** saves; **Start scan** saves and starts.
- **▶ Scan**: starts the configured scan; the button turns red, "■ Stop scan". The status bar shows the current frequency or memory (without filling the message history).
- The scan sets the rig and the WebSDR, like entering a frequency. It stops as soon as the rig goes into **transmit** (SDRLink's PTT or the rig's); it cannot start while transmitting.

## Recording (REC)

- **REC** button, on the audio row, right after the last volume shown (it moves left when TRX volume, ExpertSDR volume or HDSDR volume are not shown): **navy blue** when idle (colour can be changed), **orange** while recording.
- One click starts recording **everything heard in the speakers** (Windows default audio output): WebSDR, ExpertSDR, rig connected to the PC, Windows sounds… The Windows volume and the Mute buttons do not change what is captured before them: what the output plays is what gets recorded.
- Another click stops and opens the **Save the recording** window:
  - **Name**: suggested from the date, time, frequency and mode at the start (editable);
  - **Folder**: `Music\SDRLink` by default, **Browse…** to choose another one (the last folder chosen is remembered);
  - **Format**: **MP3** (default, 128 kbit/s, Windows encoder), **WAV** (lossless, large) or **FLAC** (lossless, about half the size);
  - **Delete** discards the recording (after confirmation).
- The file is written in the background; the status bar shows the progress, then "Recording saved". If it fails, the window comes back: the audio is not lost.
- **Right-click REC**: choice of the **rig's sound card** (its input in Windows, for example "USB AUDIO CODEC" or "USB Sound Device"), or "None (always the speakers)". This card is recorded **instead of the speakers** only if you are listening to the rig alone when you press REC: **Mute WebSDR** on (red), **Mute ExpertSDR** on (red) if ExpertSDR is running and **Mute HDSDR** on (red) if HDSDR is playing. Otherwise, the speakers are recorded. If the card is unplugged, the speakers are recorded and the status bar says so. The choice is kept.
- **Right-click REC > REC button colour**: colour of the button when idle (navy blue by default, blue, sky blue, teal, green, red, burgundy, purple, pink, brown, grey, black, yellow, or any colour with "Other colour…"). The text turns black on light colours; while recording, the button stays orange. The choice is kept.
- **Right-click REC > REC button width**: **Narrow** (a rectangle twice as wide as it is high, default) or **Wide** (four times as wide as it is high), always as high as the Mute buttons, with "REC" centred. The choice is kept.
- Silences are kept (the file length is the real duration). Closing SDRLink during a recording offers to save it before quitting.

## Frequency memories

- **Memories** button (frequency row, after Save): opens the memories window.
  - **Store the current frequency…**: same window as for adding a favorite, with **Name** (nickname, optional), **Frequency** in kHz, **Mode** (or "mode unchanged") and **List**.
  - **Clicking a memory** changes the frequency at once (rig and WebSDR) and the mode if it is stored; the window stays open so you can move on to another one. Enter does the same.
  - **Edit…**, **Remove…** (or the Delete key).
  - Memory lists as for favorites: **List** menu (or "All lists"), right-click to rename, **New list** (10 at most), **Delete list…** (with its memories, after confirmation).
- **Right-click** Memories: store the current frequency without opening the window.
- **▾** button on the right: the **last 20 memories used**, the most recent at the top. A memory that has a name (nickname) is shown by its name, otherwise by its frequency. One click goes there. At the bottom: Memories… and Clear this list.
- Memories are kept in the settings (`settings.ini`, `Mem=` lines).

## Status bar (messages at the bottom of the window)

- **Right-click** the status bar: the **last 20 messages**, the most recent at the top, with their time.
- Clicking a message **hides that type of message** (ticked ✓): it is no longer shown in the bar, but stays in the list of last messages. Messages that differ only by a name or a number (""A" added to "HF"", ""B" added to "VHF"") are of the same type. Clicking again shows it again.
- **Show all hidden messages again**: cancels all hiding. The hidden types are kept for the next launch.
- The message "SDRLINK cable: no virtual audio cable found" disappears as soon as the cable is set to **Off**.

## Settings import / export

**Import/Export ▾** button (1st row, after "Check for updates"): to get exactly the same configuration on another computer, or to keep a backup. Right-click: button colour.
- All SDRLink settings are in a single file: **`%APPDATA%\SDRLink\settings.ini`** (for example `C:\Users\<name>\AppData\Roaming\SDRLink\settings.ini`). The log `sdrlink.log` is next to it.
- **Export all settings…**: saves this file wherever you want (Documents by default, named `SDRLink-reglages-<date>.ini`), with the current state: interface and rig (COM port, baud rate, TCI, HRD, Hamlib…), synchronisation, offset, favorites and their lists, memories, frequency history, language, dark mode and palette, button colours, text size, REC (folder, sound card, colour, width), SDRLINK cable, scan, volumes, window size and position, etc.
- **Import settings…**: choose an exported file. After confirmation, all current settings are replaced and SDRLink restarts with the configuration from the file. The previous settings are kept in `settings.avant-import.ini` (same folder): to go back, import that file.
- A file that does not contain SDRLink settings is refused (message in the status bar).
- On another computer, check what depends on the machine: rig COM port, sound cards (SDRLINK cable, rig sound card for REC), rigctld path, recordings folder. A missing rig sound card is shown as "not connected" in the REC menu; if the window position falls outside the screens, it is ignored.
- Browsing data of the WebSDR pages (cookies, cache) is not part of the settings and is not exported.

## Updates

**Check for updates** box (1st row, ticked by default): at startup, if the Internet answers, SDRLink looks up the latest version published on GitHub. If there is a newer one, an orange link "⚠ Update Vx.y available" appears at the bottom right of the window, in the status bar (also visible in reduced and mini views): one click opens the download page. Ticking the box runs the check immediately.

## Troubleshooting

**OmniRig does not work** (no frequency, error on connection): use **OmniRig version 1** (not version 2), run **as administrator** (right-click OmniRig > Run as administrator).

Settings: `%APPDATA%\SDRLink\settings.ini`. Log: `%APPDATA%\SDRLink\sdrlink.log`. The log is cleared at each launch and contains the connection status and, for HRD, the list of modes received.

Message "Unhandled exception… 0x8007139F" when clicking a `.html` file in the "Import favorites" window: it comes from the **Monaco** preview of **PowerToys**, not from SDRLink (click Continue, the import works). To avoid it: **Alt+P** in the open dialog (hides the preview pane), or in PowerToys > File Explorer add-ons, disable the source code preview (Monaco).

## Version history

- **1.15**
  - **HDSDR**: when HDSDR is running on the PC, **Mute HDSDR** (after the other Mute buttons) and **HDSDR volume** (slider and %) appear on the audio row, and disappear when it is closed. The audio is muted and set through the Windows volume mixer, with no extra COM port; HDSDR can stay connected to HRD. ExpertSDR works as before and can run at the same time;
  - **⇄** also switches with HDSDR; right-click ⇄: up to **two** mutes left aside (check boxes), to switch only between the remaining receivers;
  - Mute HDSDR: colours when muted / sound on via right-click, like the other Mute buttons;
  - REC: the rig sound card is used if Mute HDSDR is also on while HDSDR is playing.
- **1.14**
  - **new rigs in direct CAT**, after comparing with the Hamlib, OmniRig and Ham Radio Deluxe lists:
    - Yaesu: FT-100/100D, FT-600, FT-650, FT-736R, FT-747GX, FT-757GX, FT-757GXII, FT-767GX, FT-840, FT-847, FT-890, FT-900, FT-920, FTdx9000, FTX-1;
    - Icom: IC-78, IC-271, IC-275, IC-375, IC-471, IC-475, IC-575, IC-703, IC-707, IC-725, IC-726, IC-728, IC-729, IC-735, IC-736, IC-737, IC-738, IC-746, IC-751/751A, IC-756, IC-756PRO, IC-756PROII, IC-761, IC-765, IC-775, IC-781, IC-820H, IC-821H, IC-905, IC-910H, IC-970, IC-1275, IC-7300MK2, IC-7400, IC-7850, IC-9100;
    - Kenwood: TS-50S, TS-60S, TS-140S, TS-440S, TS-450S, TS-680S, TS-690S, TS-711, TS-790, TS-811, TS-930S, TS-940S, TS-950S, TS-950SDX, TS-B2000;
    - Xiegu: X108G, X5105;
  - model list **sorted by number** for each brand;
  - Xiegu G90: suggested CI-V address corrected to 88 (factory value according to Hamlib);
  - older Kenwoods: mode read from the `IF;` answer when the rig does not know `MD;`.
- **1.13**
  - **background colour of your choice by right-click** on almost every button (Connect, Open log, Text size, SDRLINK cable, Scan setup, Scan, PTT, RIG, WEBSDR, mode buttons, QSY, Save, Memories, Go, Back, Reload, favorites, Find WebSDRs, the three Mutes), one colour per state for buttons that have two; existing right-click menus are kept;
  - **Import/Export** button: exports all settings (interface, favorites, memories, colours…) to a file, and imports them on another computer (SDRLink restarts, previous settings kept in settings.avant-import.ini).
- **1.12**
  - **REC** button moved to the audio row, right after the last volume shown (no empty space when ExpertSDR or the rig volume are not shown), with the same look and height as the Mute buttons;
  - right-click REC: **button colour** when idle (navy blue by default) and **width** (narrow or wide);
  - **SDR / LINK** icon in the taskbar, the title bar and File Explorer.
- **1.11**
  - **REC** button: records the audio heard in the speakers, then saves it as MP3, WAV or FLAC (name, folder and format of your choice); right-click: rig sound card, recorded when only the rig is being listened to (Mute WebSDR and Mute ExpertSDR on);
  - **PTT view** box: a very small window with only the PTT button, returning to the previous view exactly as it was;
  - manual: if OmniRig does not work, use OmniRig version 1 run as administrator.
- **1.10**
  - unticking "Reduced view" or "Mini view" always returns to normal view;
  - audio row reorganised: ⇄, Mute WebSDR, Mute TRX, Mute ExpertSDR, then WebSDR volume, TRX volume, ExpertSDR volume (each label before its slider; the order no longer gets mixed up when the rig or ExpertSDR appear); only the available audio sources are shown, with no empty space, and ⇄ only if there are at least two sources;
  - new Yaesu rigs: FT-990, FT-1000, FT-1000D, FT-1000MP, FT-1000MP Mark-V and Mark-V Field (direct CAT, Hamlib, and HRD / OmniRig through their own rig selection).
- **1.9**
  - at startup, the rig no longer changes frequency or mode: only the WebSDR tunes to it (if the sync mode calls for it);
  - frequency or memory **scan**: "Scan setup" and "▶ Scan" buttons;
  - "Reduced view" renamed **Mini view**; new **Reduced view** starting from the Sync row, page included; each view gets back its size.
  - fix: with enlarged text (more than 100%) and the window maximised, the window reopened cut off in height.
- **1.8**
  - **frequency memories**: name (nickname), frequency, mode, up to 10 lists; instant tuning with one click; ▾ button with the last 20 used;
  - **Text size** window (70 to 200%);
  - Mute buttons green when the audio is passing, red when muted (white text); ⇄ button truly square;
  - up to **10 favorite lists**, "New list" and "Delete list…" buttons; export and import (Firefox HTML, CSV) with all lists, lists created on import if needed;
  - right-click the status bar: last 20 messages, message types can be hidden;
  - the "no virtual audio cable found" message no longer stays displayed when the SDRLINK cable is off.
- **1.7**
  - favorites: export and import compatible with **Firefox**, by list (SDRLink folder with one sub-folder per list); import puts each favorite back in its own list instead of putting everything into a single one.
  - an unexpected error is recorded in detail in the log ("Erreur inattendue" line) and reported at the bottom of the window, without a dialog box.
- **1.6**
  - ⇄ button: the audio switches from one receiver to the next (WebSDR, TRX, ExpertSDR); right-click to leave one mute aside;
  - "Check for updates" box and link to the new version in the status bar.
- **1.5**
  - first start: WebSDR search run once, if the Internet answers;
  - the list to save to is asked for each time a favorite is added, and a favorite already in the list is not added;
  - "Check favorites" removes duplicates;
  - the search no longer offers WebSDRs already in favorites;
  - "Listen to selection" in the favorite manager;
  - ⇄ button that swaps Mute WebSDR and Mute TRX;
  - Mute ExpertSDR button and ExpertSDR volume slider when ExpertSDR is running (Windows mixer or TCI), TCI port via right-click;
  - TRX volume and Mute TRX hidden when the rig volume is not remotely controlled;
  - HRD: rig volume tracked when you change rig in HRD; Hamlib: the volume is no longer greyed out after a single missed reading.
- **1.4.1**: Yaesu FT-857, FT-857D, FT-897 and FT-897D.
- **1.4**
  - **Hamlib (rigctld)** interface, with automatic start of rigctld;
  - 23 European languages;
  - "Add to favorites" and "Remove from favorites" only apply to the list shown.
- **1.3**
  - "POSTE" becomes "RADIO" (banner and Sync menu) — shown as "RIG" in the English interface;
  - empty favorite lists are no longer offered in the WebSDR bar;
  - SDRLINK cable active in all modes, "SDRLINK cable" button and renaming of the input to SDRLINK;
  - CW button; DIGI and CW toggle between upper and lower sideband;
  - automatic notch (DNF).

- **1.2**
  - DIGI mode with the WebSDR audio copied to a virtual sound card (WSJT-X);
  - mouse wheel according to the sync direction; in "WebSDR → Rig", the rig tunes to the WebSDR;
  - rig volume via HRD (AF gain slider);
  - reduced view from "Sync" to "Volume";
  - TX timeouts of 15, 20 and 25 min; "Check all" in favorites;
  - fix for DATA mode on Kenwoods.
- **1.1**
  - favorite lists (4 renamable lists, moving between lists);
  - TX safety timeout via right-click on PTT;
  - WebSDR re-tuned when the sync mode is chosen, and offset monitoring;
  - automatic profile switching on multi-band OpenWebRX receivers.
- **1.0** — first version.
