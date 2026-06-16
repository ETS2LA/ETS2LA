from ETS2LA.UI import (
    ETS2LAPage,
    styles,
    Container,
    Text,
    Link,
    ButtonWithTitleDescription,
    AdSense,
    Space,
    Markdown,
    Button,
)
from ETS2LA.Networking.cloud import (
    GetUserCount,
    GetUserTimeInfo,
    GetUniqueUsers,
    token,
    user_id,
    GetUsername,
)
from ETS2LA.UI.utils import SendPopup

from ETS2LA.Utils.translator import _, ngettext, languages, parse_language
from ETS2LA.Networking.Servers.webserver import mainThreadQueue
from Modules.SDKController.main import SCSController
from ETS2LA.Networking.Servers import notifications, webserver
from ETS2LA.Settings import GlobalSettings
from ETS2LA.Utils.Game import path as game
from ETS2LA.Utils.version import Update
from langcodes import Language
from threading import Thread
import hashlib
import time
import os

settings = GlobalSettings()

contributors = [
    {
        "name": "Tumppi066",
        "description": _("Lead developer and creator of ETS2LA, backend & frontend."),
        "links": [
            ["Github", "https://github.com/Tumppi066"],
            ["Youtube", "https://www.youtube.com/@Tumppi066"]
        ],
    },
    {
        "name": "Glas42",
        "description": _(
            "Navigation Detection, Traffic Light Detection, ETS2LA Lite, 'co-owner'"
        ),
        "links": [["Github", "https://github.com/OleFranz"]],
    },
    {
        "name": "DylanBPY",
        "description": _(
            "Various additions and improvements, lagacy Object Detection AI models & development"
        ),
        "links": [
            ["Github", "https://github.com/DylanBPY"],
            ["Youtube", "https://www.youtube.com/@DylanBPY"],
        ],
    },
    {
        "name": "Roccovax",
        "description": _(
            "ETS2LA game-side SDK developer. Vehicles, Traffic Lights, Camera data and more direct from the game."
        ),
        "links": [["Github", "https://github.com/DarioWouters"]],
    },
    {
        "name": "truckermudgeon",
        "description": _("Game data extraction and processing"),
        "links": [],
    },
    {
        "name": "Cloud",
        "description": _("Linux & Unix Port, various improvements and bug fixes"),
        "links": [],
    },
    {
        "name": "ziakhan4505",
        "description": _("ETS2LA SDK C++ developer, Linux & Unix Port, bug fixes"),
        "links": [],
    },
    {
        "name": "WhyTrevorWhy",
        "description": _("Help with Navigate on ETS2LA & Map"),
        "links": [],
    },
    {
        "name": "mimi89999",
        "description": _("scs-sdk-controller developer"),
        "links": [],
    },
    {
        "name": "zhaoyj",
        "description": _("3D models for the visualization"),
        "links": [],
    },
    {
        "name": "JimJokes",
        "description": _("Data extractor development, support for modded maps."),
        "links": [],
    },
    {
        "name": "playzzero97",
        "description": _(
            "Discord rich presence plugin developer, slight backend updates."
        ),
        "links": [],
    },
    {
        "name": "Lun",
        "description": _("Chinese translation, bug fixes"),
        "links": [["Discord", "https://discordapp.com/users/832636302402256898"]],
    },
    {
        "name": "MRUIAW",
        "description": _("Chinese translations, bug fixes"),
        "links": [["BiliBili", "https://space.bilibili.com/357816575"]],
    },
    {
        "name": "PiggyWu981",
        "description": _("Automatic offset calculations for the Map plugin."),
        "links": [
            ["GitHub", "https://github.com/Piggywu981"],
            ["BiliBili", "https://space.bilibili.com/355054416"],
            ["Discord", "https://discordapp.com/users/763642553412223008"],
        ],
    },
    {
        "name": "Sheng FAN",
        "description": _(
            "Discord server support, Chinese-English translator developer."
        ),
        "links": [["Github", "https://github.com/fred913"]],
    },
    {
        "name": "goodnightan",
        "description": _("Maintaining CN mirrors for ETS2LA services."),
        "links": [["BiliBili", "https://space.bilibili.com/525984002"]],
    },
    {"name": "Piotrke", "description": _("Game hooks"), "links": []},
    {"name": "DTheIcyDragon", "description": _("Bug fixes"), "links": []},
    {
        "name": "Roman Sonnik",
        "description": _("Bug fixes"),
        "links": [["Github", "https://github.com/RomanSonnik"]],
    },
    {"name": "atac_helicopter", "description": _("Bug fixes"), "links": []},
    {"name": "ғʟᴇxғʟᴇxᴇɴ", "description": _("Bug fixes"), "links": []},
    {"name": "LookAtYourSkill", "description": _("Bug fixes"), "links": []},
    {"name": "ViSzKe", "description": _("Bug fixes"), "links": []},
]

games = game.FindSCSGames()
game_versions = [game.GetVersionForGame(found_game) for found_game in games]
target_path = "\\bin\\win_x64\\plugins"

data_versions = os.listdir("ETS2LA/Assets/DLLs")
files_for_version = {}
hashes_for_version = {}
for version in data_versions:
    files_for_version[version] = os.listdir(f"ETS2LA/Assets/DLLs/{version}")
    files_for_version[version].pop(files_for_version[version].index("sources.txt"))
    hashes_for_version[version] = {}
    for file in files_for_version[version]:
        with open(f"ETS2LA/Assets/DLLs/{version}/{file}", "rb") as f:
            hashes_for_version[version][file] = hashlib.sha256(f.read()).hexdigest()


def needs_update(game: str) -> bool:
    if not os.path.exists(game + target_path):
        return False  # not even installed

    missing_files = []
    for file in files_for_version[game_versions[games.index(game)]]:
        if not os.path.exists(game + target_path + "\\" + file):
            missing_files.append(file)

    if len(missing_files) != 0 and len(missing_files) != len(
        files_for_version[game_versions[games.index(game)]]
    ):
        return True  # some files are missing, but not all of them
    if len(missing_files) == len(files_for_version[game_versions[games.index(game)]]):
        return False  # all files are missing, which means the user has never installed the plugin

    game_hashes = {}
    for file in files_for_version[game_versions[games.index(game)]]:
        with open(game + target_path + "\\" + file, "rb") as f:
            game_hashes[file] = hashlib.sha256(f.read()).hexdigest()

    for file, filehash in hashes_for_version[game_versions[games.index(game)]].items():
        if file not in game_hashes or game_hashes[file] != filehash:
            return True  # file is missing or has a different hash, which means it is outdated

    return False  # everything is fine, no update needed


class Page(ETS2LAPage):
    url = "/about"
    game_needs_update = {}
    refresh_rate = 60
    show_kofi = False

    user_count = "..."
    unique_users = "..."
    user_time = "..."

    def fix_wipers(self):
        print(_("Fixing wipers (5s timer)"))
        controller = SCSController()

        start_time = time.perf_counter()
        while time.perf_counter() - start_time < 5:
            SendPopup(
                _("Fixing wipers in {0} seconds...").format(
                    5 - int(time.perf_counter() - start_time)
                ),
                "info",
            )
            time.sleep(1)

        controller.wipers0 = True
        time.sleep(0.5)
        controller.wipers0 = False
        print(_("Wipers should be fixed now."))
        SendPopup(_("Wipers should be fixed now."), "success")

    def update(self, *args, **kwargs):
        print(_("Triggering update"))
        mainThreadQueue.append([Update, [], {}])

    def open_event(self):
        super().open_event()
        self.game_needs_update = {}
        for path in games:
            try:
                self.game_needs_update[path] = needs_update(path)
            except Exception:
                pass

    def seconds_to_time(self, seconds):
        if not isinstance(seconds, (int, float)):
            return seconds

        if not seconds:
            return ngettext("{0} minute", "{0} minutes", 0).format(0)
        if seconds == 0:
            return ngettext("{0} minute", "{0} minutes", 0).format(0)

        hours = round(seconds // 3600)
        minutes = round((seconds % 3600) // 60)
        if hours == 0:
            return ngettext("{0} minute", "{0} minutes", minutes).format(minutes)
        elif minutes == 0:
            return ngettext("{0} hour", "{0} hours", hours).format(hours)
        else:
            return _("{0} and {1}").format(
                ngettext("{0} hour", "{0} hours", hours).format(hours),
                ngettext("{0} minute", "{0} minutes", minutes).format(minutes),
            )

    def handle_hide_kofi(self):
        self.show_kofi = False
        self.need_update = True

    def open_sdk_page(self):
        webserver.mainThreadQueue.append(
            [
                notifications.navigate,
                [
                    "/settings/sdk",
                    "ETS2LA",
                ],
                {},
            ]
        )

    def init(self):
        thread = Thread(target=self.data_update_thread, daemon=True)
        thread.start()

    def data_update_thread(self):
        while True:
            time.sleep(1)
            if self.is_open():
                self.user_count = _("{0} users").format(GetUserCount())
                self.need_update = True
                self.unique_users = _("{0} unique users").format(GetUniqueUsers())
                self.need_update = True
                self.user_time = GetUserTimeInfo()
                self.need_update = True
                time.sleep(60)

    def render(self):
        ads = settings.ad_preference
        if ads >= 1:
            with Container(
                style=styles.FlexVertical()
                + styles.Gap("4px")
                + styles.Padding("40px 0px 0px 80px")
                + styles.Classname("relative")
            ):
                AdSense(
                    client="ca-pub-6002744323117854",
                    slot="6129868382",
                    style=styles.Style(
                        display="inline-block", width="900px", height="90px"
                    ),
                )
                Text(
                    "Ads can be disabled in the settings, they support development.",
                    styles.Classname("text-muted-foreground")
                    + styles.Style(font_size="9px"),
                )
                            
        with Container(
                styles.FlexVertical()
                + styles.Padding("40px 0px 0px 80px")
                + styles.MaxWidth("900px")
            ):
                with Container(
                    styles.Classname("w-full p-4 border rounded-md")
                    # + styles.Style(background_color="#c45635")
                ):
                    Text(
                        _("Announcement"),
                        styles.Classname("font-bold")
                        + styles.Style(
                            color="#e9e9e9",
                            text_shadow="1px 1px 2px #00000080",
                        ),
                    )
                    Space(styles.Height("4px"))
                    Markdown(
                        _(
                            "This version of ETS2LA will soon be superceded. Please prepare to reinstall ETS2LA once the new version releases, there will not be an automatic updater! You can monitor the links below for updates."
                        ),
                        styles.Style(
                            color="#e9e9e9",
                            text_shadow="1px 1px 2px #00000080",
                        ),
                    )
                    Space(styles.Height("8px"))
                    with Container(
                        styles.FlexHorizontal()
                        + styles.Gap("10px")
                        + styles.Classname("items-center")
                    ):
                        Link(
                            _("ETS2LA Discord"),
                            "https://ets2la.com/discord",
                            styles.Classname("text-xs hover:underline")
                            + styles.Style(
                                color="#e9e9e9",
                                text_shadow="1px 1px 2px #00000080",
                            ),
                        )
                        Link(
                            _("ETS2LA Website"),
                            "https://docs.ets2la.com/blog",
                            styles.Classname("text-xs hover:underline")
                            + styles.Style(
                                color="#e9e9e9",
                                text_shadow="1px 1px 2px #00000080",
                            ),
                        )
                        Link(
                            _("Tumppi066 YouTube"),
                            "https://www.youtube.com/@Tumppi066",
                            styles.Classname("text-xs hover:underline")
                            + styles.Style(
                                color="#e9e9e9",
                                text_shadow="1px 1px 2px #00000080",
                            ),
                        )

        with Container(
            style=styles.FlexVertical()
            + styles.Padding(
                "15px 0px 0px 80px"
            )
            + styles.MaxWidth("900px")
        ):
            if any(self.game_needs_update.values()):
                for game, needs_update in self.game_needs_update.items():
                    if needs_update:
                        with Container(
                            style=styles.FlexVertical()
                            + styles.Gap("5px")
                            + styles.Classname("p-4 rounded-lg border")
                            + styles.Margin("0px 0px 15px 0px")
                            + styles.Style(background_color="#584818")
                        ):
                            Text(
                                _("Notice!"),
                                styles.Classname("font-semibold")
                                + styles.Style(color="#e9e9e9"),
                            )
                            Text(
                                _(
                                    "An SDK for {0} is outdated. Please reinstall it to update to the latest version in the SDK settings."
                                ).format(os.path.basename(game)),
                                styles.Style(color="#BCBCBC"),
                            )
                            with Button(
                                action=self.open_sdk_page,
                                type="link",
                                style=styles.Width("max-content"),
                            ):
                                Text(
                                    _("Open SDK Settings"),
                                    styles.Classname("text-xs hover:underline")
                                    + styles.Style(color="#e9e9e9"),
                                )

            Text(_("About"), styles.Title())
            Space()
            Text(
                _(
                    "ETS2LA is a project that aims to provide an easy to use self driving solution for ETS2 and ATS, if you want to learn more then you can visit the github page or the wiki via the sidebar."
                ),
                styles.Description(),
            )
            Space(style=styles.Height("14px"))

            with Container(style=styles.FlexVertical()):
                Text(_("Statistics"), styles.Title())
                Space()
                with Container(style=styles.FlexVertical() + styles.Gap("6px")):
                    with Container(style=styles.FlexHorizontal()):
                        Text(f"{_('Users online:')} ")
                        Text(self.user_count, styles.Description())
                    with Container(style=styles.FlexHorizontal()):
                        Text(f"{_('Past 24 hours:')} ")
                        Text(
                            self.unique_users,
                            styles.Description(),
                        )
                    with Container(style=styles.FlexHorizontal() + styles.Gap("4px")):
                        Text(f"{_('Your usage time:')}  ")
                        Text(
                            self.seconds_to_time(
                                self.user_time.get("time_used", 0)
                                if isinstance(self.user_time, dict)
                                else self.user_time
                            ),
                            styles.Description(),
                        )
                        Text(
                            ngettext(
                                "over {count} session",
                                "over {count} sessions",
                                self.user_time.get("sessions", 0)
                                if isinstance(self.user_time, dict)
                                else 0,
                            ).format(
                                count=self.user_time.get("sessions", 0)
                                if isinstance(self.user_time, dict)
                                else "..."
                            ),
                            styles.Description(),
                        )
                    if token is None:
                        with Container(style=styles.FlexVertical() + styles.Gap("6px")):
                            Space(style=styles.Height("10px"))
                            Text(f"{_('You are not logged in.')} ")
                            Text(
                                _("Your anonymous user ID is: {0}").format(
                                    str(user_id)
                                ),
                                styles.Description(),
                            )
                    else:
                        with Container(style=styles.FlexVertical() + styles.Gap("6px")):
                            Space(style=styles.Height("10px"))
                            Text(f"{_('You are logged in.')} ")
                            Text(
                                _("Welcome back, {0}!").format(
                                    str(GetUsername(force_refresh=True))
                                ),
                                styles.Description(),
                            )

                    if (
                        not self.unique_users
                        or str(self.unique_users).split(" ")[0] == "0"
                    ) or (
                        not self.user_count or str(self.user_count).split(" ")[0] == "0"
                    ):
                        with Container(
                            style=styles.FlexVertical()
                            + styles.Margin("6px 0")
                            + styles.Classname("border rounded-md bg-input/10 p-4")
                        ):
                            Text(
                                _(
                                    "The ETS2LA backend might be partially unavailable, please mind any erroneous values or missing statistics!"
                                ),
                                style=styles.Classname("font-semibold"),
                            )
                            Text(
                                _(
                                    "We are working to restore full functionality as soon as possible. Hang on tight, this might take anywhere from a few minutes to several days!"
                                ),
                                style=styles.Description(),
                            )

                Space(style=styles.Height("10px"))
                with Container(style=styles.FlexVertical() + styles.Gap("16px")):
                    Text(_("Contributors"), styles.Title())
                    for contributor in contributors:
                        with Container(style=styles.FlexVertical() + styles.Gap("4px")):
                            with Container(
                                style=styles.FlexHorizontal()
                                + styles.Gap("10px")
                                + styles.Padding("0px 0px 0px 0px")
                                + styles.Classname("items-center")
                            ):
                                Text(contributor["name"], styles.PlainText())
                                for link in contributor["links"]:
                                    Link(
                                        link[0],
                                        link[1],
                                        style=styles.Classname(
                                            "text-xs hover:underline"
                                        ),
                                    )
                            Text(contributor["description"], styles.Description())

                Space(style=styles.Height("10px"))
                with Container(style=styles.FlexVertical() + styles.Gap("16px")):
                    Text(_("Translations"), styles.Title())
                    for language in languages:
                        if not isinstance(language, Language):
                            continue

                        with Container(
                            style=styles.FlexVertical()
                            + styles.Gap("4px")
                            + styles.Classname("items-start")
                        ):
                            with Container(
                                style=styles.FlexHorizontal()
                                + styles.Gap("10px")
                                + styles.Padding("0px 0px 0px 0px")
                                + styles.Classname("items-center")
                            ):
                                Text(
                                    language.display_name(
                                        language.language
                                    ).capitalize(),
                                    styles.PlainText(),
                                )
                                Text(
                                    "(" + language.display_name() + ")",
                                    styles.Description() + styles.Classname("text-xs"),
                                )
                            with Container(
                                style=styles.FlexHorizontal() + styles.Gap("10px")
                            ):
                                Link(
                                    _("List Contributors"),
                                    f"https://weblate.ets2la.com/user/?q=translates:{parse_language(language)}%20contributes:ets2la/backend",
                                    styles.Classname(
                                        "text-xs text-muted-foreground hover:underline"
                                    ),
                                )
                                Text("-")
                                Link(
                                    _("Help Translate"),
                                    f"https://weblate.ets2la.com/projects/ets2la/backend/{parse_language(language)}",
                                    styles.Classname(
                                        "text-xs text-muted-foreground hover:underline"
                                    ),
                                )

                Space(style=styles.Height("10px"))
                with Container(style=styles.FlexVertical() + styles.Gap("10px")):
                    Text(_("Support Development"), styles.Title())
                    with Container(style=styles.FlexVertical() + styles.Gap("10px")):
                        with Container(
                            style=styles.FlexVertical()
                            + styles.Gap("6px")
                            + styles.Padding("0px 0px 0px 0px")
                        ):
                            Text(
                                "• "
                                + _(
                                    "If you like the project and want to support the development, you can do so by donating via Ko-Fi."
                                ),
                                styles.Description(),
                            )
                            Link(
                                "Ko-Fi",
                                "https://ko-fi.com/tumppi066",
                                style=styles.Classname("text-xs hover:underline w-max")
                                + styles.Padding("0px 0px 0px 7px"),
                            )
                        with Container(
                            style=styles.FlexVertical()
                            + styles.Gap("6px")
                            + styles.Padding("0px 0px 0px 0px")
                        ):
                            Text(
                                "• "
                                + _(
                                    "If you want to contribute, then I recommend joining the discord server and checking out the github page."
                                ),
                                styles.Description(),
                            )
                            Link(
                                "Discord",
                                "https://ets2la.com/discord",
                                style=styles.Classname("text-xs hover:underline w-max")
                                + styles.Padding("0px 0px 0px 7px"),
                            )
                            Link(
                                "GitHub",
                                "https://github.com/ETS2LA/Euro-Truck-Simulator-2-Lane-Assist",
                                style=styles.Classname("text-xs hover:underline w-max")
                                + styles.Padding("0px 0px 0px 7px"),
                            )
                        with Container(
                            style=styles.FlexVertical()
                            + styles.Gap("6px")
                            + styles.Padding("0px 0px 0px 0px")
                        ):
                            Text(
                                "• "
                                + _(
                                    "If you want to contribute to the translations, then you can do so by joining the discord and contacting a moderator."
                                ),
                                styles.Description(),
                            )
                            Link(
                                "Discord",
                                "https://ets2la.com/discord",
                                style=styles.Classname("text-xs hover:underline w-max")
                                + styles.Padding("0px 0px 0px 7px"),
                            )

                Space(style=styles.Height("10px"))
                with Container(style=styles.FlexVertical() + styles.Gap("16px")):
                    Text(_("Utils"), styles.Title())

                    ButtonWithTitleDescription(
                        self.fix_wipers,
                        title=_("Fix Wipers"),
                        description=_(
                            "Did your wipers get stuck? Click the button and alt tab to the game. They should turn off in 5 seconds."
                        ),
                        text=_("Activate"),
                    )

                    ButtonWithTitleDescription(
                        self.update,
                        title=_("Force an update"),
                        description=_(
                            "Do you think there should've been an update? Click this button and the app will restart and check for them."
                        ),
                        text=_("Update"),
                    )

            Space(style=styles.Height("60px"))
