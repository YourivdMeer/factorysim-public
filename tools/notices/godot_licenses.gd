# Writes the licence text, copyright list and licence texts that Godot itself carries
# (the same data as Help > About > Third-party Licenses) to the file named after "--".
# Run by tools/notices/build_notices.py; not part of the app.
extends SceneTree
func _init():
	var out := ""
	out += Engine.get_license_text() + "\n\n"
	var info := Engine.get_license_info()
	for part in Engine.get_copyright_info():
		out += "------------------------------------------------------------\n"
		out += "Component: " + str(part["name"]) + "\n"
		for p in part["parts"]:
			out += "Files: " + ", ".join(PackedStringArray(p["files"])) + "\n"
			for c in p["copyright"]:
				out += "Copyright: " + str(c) + "\n"
			out += "License: " + str(p["license"]) + "\n"
		out += "\n"
	out += "============================================================\nLicense texts\n============================================================\n"
	var keys := info.keys()
	keys.sort()
	for k in keys:
		out += "\n------------------------------------------------------------\nLicense: " + str(k) + "\n\n" + str(info[k]) + "\n"
	var f := FileAccess.open(OS.get_cmdline_user_args()[0], FileAccess.WRITE)
	f.store_string(out)
	f.close()
	quit()
