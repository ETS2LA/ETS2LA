from ETS2LA.UI import ETS2LAPage, styles, Container, Text, Button, Space, Link, Markdown

from ETS2LA.Networking.Servers.webserver import mainThreadQueue
from ETS2LA.Utils.version import CheckForUpdate, Update
from ETS2LA.Utils.translator import _, ngettext

from datetime import datetime
import time

last_update_check = 0
last_updates = []


class Page(ETS2LAPage):
    dynamic = True
    url = "/updater"
    refresh_rate = 30

    def update(self, *args, **kwargs):
        mainThreadQueue.append([Update, [], {}])

    def open_event(self):
        super().open_event()
        self.reset_timer()

    def time_since(self, target_time):
        diff = time.time() - target_time
        if diff < 60:
            return ngettext(
                "{seconds} second ago", "{seconds} seconds ago", int(diff)
            ).format(seconds=int(diff))
        elif diff < 3600:
            return ngettext(
                "{minutes} minute ago", "{minutes} minutes ago", int(diff / 60)
            ).format(minutes=int(diff / 60))
        elif diff < 86400:
            return ngettext(
                "{hours} hour ago", "{hours} hours ago", int(diff / 3600)
            ).format(hours=int(diff / 3600))
        else:
            return ngettext(
                "{days} day ago", "{days} days ago", int(diff / 86400)
            ).format(days=int(diff / 86400))

    def render(self):
        global last_update_check, last_updates

        if time.time() - last_update_check > 10:
            last_update_check = time.time()
            updates = CheckForUpdate()
            last_updates = updates
        else:
            updates = last_updates

        with Container(
            styles.FlexVertical()
            + styles.Padding("24px")
            + styles.Gap("24px")
            + styles.Margin("30px 60px 60px 60px")
        ):
            if updates == []:
                Text(
                    _("You have a local commit that is waiting to be pushed."),
                    styles.Description() + styles.Classname("text-xs"),
                )
            elif not updates:
                Text(
                    _(
                        "No updates available. (It might take up to a minute for the page to update after a new commit)"
                    ),
                    styles.Description(),
                )
                with Button(self.update):
                    Text(_("Update Anyway"), styles.Classname("font-semibold"))
            else:
                with Button(self.update):
                    Text(
                        ngettext(
                            "Restart and apply {count} update",
                            "Restart and apply {count} updates",
                            len(updates),
                        ).format(count=len(updates)),
                        styles.Classname("font-semibold"),
                    )
                Space(styles.Height("20px"))

                updates = updates[::-1]
                current_day = None
                for update in updates[:20]:
                    local_time = datetime.fromtimestamp(update["time"]).strftime(
                        "%Y-%m-%d %H:%M:%S"
                    )
                    if local_time.split(" ")[0] != current_day:
                        current_day = local_time.split(" ")[0]
                        border_bottom = styles.Classname("border-b w-full")
                        with Container(
                            styles.FlexHorizontal()
                            + styles.Padding("0")
                            + styles.Classname("justify-between items-center")
                        ):
                            with Container(
                                styles.FlexHorizontal()
                                + styles.Padding("0")
                                + border_bottom
                            ):
                                ...
                            with Container(styles.FlexVertical() + styles.Padding("0")):
                                Text(
                                    local_time.split(" ")[0],
                                    styles.Classname("text-xs font-bold min-w-max")
                                    + styles.Description(),
                                )
                            with Container(
                                styles.FlexHorizontal()
                                + styles.Padding("0")
                                + border_bottom
                            ):
                                ...

                    with Container(
                        styles.FlexVertical()
                        + styles.Classname(
                            "shadow-md bg-input/10 border rounded-md p-4"
                        )
                    ):
                        with Container(
                            styles.FlexHorizontal()
                            + styles.Padding("0")
                            + styles.Classname("items-center")
                        ):
                            Text(
                                update["author"],
                                styles.Description() + styles.Classname("text-xs"),
                            )
                            with Container(
                                styles.FlexHorizontal()
                                + styles.Padding("0")
                                + styles.Classname("flex justify-between w-full")
                            ):
                                Text(
                                    update["message"],
                                    styles.Classname("text-sm font-semibold"),
                                )
                                Link(
                                    _("View Changes"),
                                    update["url"],
                                    styles.Classname("text-xs font-light"),
                                )

                        if update["description"] != "":
                            Markdown(update["description"])

                        Space(styles.Height("4px"))
                        Text(
                            local_time
                            + f"  -  {self.time_since(update['time'])}  -  {update['hash'][:9]}",
                            styles.Description() + styles.Classname("text-xs"),
                        )

                Text(
                    _(
                        "Only the last 20 updates are shown. You can check the repository for further history."
                    ),
                    styles.Description() + styles.Classname("text-xs"),
                )
