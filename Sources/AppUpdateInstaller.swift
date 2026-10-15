import AppKit
import Darwin

enum AppUpdateArchive {
    // Inspect the ZIP directory before extraction. Updates contain only ordinary
    // files/directories under AIpet.app, with bounded expanded size and no symlinks.
    static func validate(_ data: Data) throws {
        func number(_ offset: Int, _ length: Int) throws -> Int {
            guard offset >= 0, length <= 4, offset <= data.count - length else { throw AppUpdateError.invalidArchive }
            return (0..<length).reduce(0) { $0 | Int(data[offset + $1]) << ($1 * 8) }
        }
        guard data.count >= 22 else { throw AppUpdateError.invalidArchive }
        var end: Int?
        for offset in stride(from: data.count - 22, through: max(0, data.count - 65557), by: -1) {
            if try number(offset, 4) == 0x06054b50, try offset + 22 + number(offset + 20, 2) == data.count { end = offset; break }
        }
        guard let end, try number(end + 4, 2) == 0, try number(end + 6, 2) == 0 else { throw AppUpdateError.invalidArchive }
        let count = try number(end + 10, 2), directorySize = try number(end + 12, 4), directory = try number(end + 16, 4)
        guard count > 0, count <= 10000, try number(end + 8, 2) == count, directory + directorySize == end else { throw AppUpdateError.invalidArchive }
        var offset = directory, expanded = 0, names = Set<String>()
        for _ in 0..<count {
            guard try number(offset, 4) == 0x02014b50, offset + 46 <= end else { throw AppUpdateError.invalidArchive }
            let nameLength = try number(offset + 28, 2), extraLength = try number(offset + 30, 2), commentLength = try number(offset + 32, 2)
            let next = offset + 46 + nameLength + extraLength + commentLength
            guard next <= end, nameLength > 0,
                  let name = String(data: data.subdata(in: offset + 46..<offset + 46 + nameLength), encoding: .utf8),
                  (name == "AIpet.app/" || name.hasPrefix("AIpet.app/")),
                  !name.contains("\\"), !name.contains(":"), !name.contains("\0"), !name.contains("\n"), !name.contains("\r"),
                  !name.split(separator: "/", omittingEmptySubsequences: true).contains(where: { $0 == "." || $0 == ".." }),
                  !name.contains("//"), names.insert(name).inserted else { throw AppUpdateError.invalidArchive }
            let kind = try (number(offset + 38, 4) >> 16) & 0xf000
            guard [0, 0x4000, 0x8000].contains(kind), try number(offset + 8, 2) & 1 == 0,
                  try [0, 8].contains(number(offset + 10, 2)) else { throw AppUpdateError.invalidArchive }
            expanded += try number(offset + 24, 4)
            guard expanded <= 512 * 1024 * 1024 else { throw AppUpdateError.invalidArchive }
            let local = try number(offset + 42, 4), compressed = try number(offset + 20, 4)
            guard local < directory, try number(local, 4) == 0x04034b50,
                  try number(local + 6, 2) == number(offset + 8, 2),
                  try number(local + 8, 2) == number(offset + 10, 2),
                  try number(local + 26, 2) == nameLength else { throw AppUpdateError.invalidArchive }
            let localNameEnd = local + 30 + nameLength
            guard localNameEnd <= directory,
                  data.subdata(in: local + 30..<localNameEnd) == data.subdata(in: offset + 46..<offset + 46 + nameLength),
                  try localNameEnd + number(local + 28, 2) + compressed <= directory else { throw AppUpdateError.invalidArchive }
            offset = next
        }
        guard offset == end, names.contains("AIpet.app/Contents/Info.plist"), names.contains("AIpet.app/Contents/MacOS/Taesik") else { throw AppUpdateError.invalidArchive }
    }
}

struct PreparedAppUpdate {
    let work: URL, destination: URL, download: URL, version: AppReleaseVersion
    func launchHelper() throws {
        let helper = download.appendingPathComponent("AIpetUpdate")
        try FileManager.default.copyItem(at: destination.appendingPathComponent("Contents/MacOS/Taesik"), to: helper)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: helper.path)
        let process = Process(); process.executableURL = helper
        process.arguments = ["--install-update", String(getpid()), work.path, destination.path, version.description, download.path]
        process.standardOutput = FileHandle.nullDevice; process.standardError = FileHandle.nullDevice
        try process.run()
    }
}

enum AppUpdateInstaller {
    static let downloads = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0].appendingPathComponent("AIpet/Updates", isDirectory: true)
    static func run(_ executable: String, _ arguments: [String]) throws {
        let process = Process(); process.executableURL = URL(fileURLWithPath: executable); process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice; process.standardError = FileHandle.nullDevice
        try process.run()
        let deadline = Date().addingTimeInterval(60)
        while process.isRunning && Date() < deadline { Thread.sleep(forTimeInterval: 0.05) }
        if process.isRunning { process.terminate(); throw AppUpdateError.invalidApp }
        guard process.terminationStatus == 0 else { throw AppUpdateError.invalidApp }
    }
    static func version(of app: URL) throws -> AppReleaseVersion {
        let manager = FileManager.default
        guard try manager.attributesOfItem(atPath: app.path)[.type] as? FileAttributeType == .typeDirectory,
              let values = try PropertyListSerialization.propertyList(from: Data(contentsOf: app.appendingPathComponent("Contents/Info.plist")), format: nil) as? [String: Any],
              values["CFBundleIdentifier"] as? String == "com.jinoisfree.taesik", values["CFBundleExecutable"] as? String == "Taesik",
              values["NSAppleEventsUsageDescription"] == nil, let text = values["CFBundleShortVersionString"] as? String,
              let version = AppReleaseVersion(text), manager.isExecutableFile(atPath: app.appendingPathComponent("Contents/MacOS/Taesik").path) else { throw AppUpdateError.invalidApp }
        return version
    }
    static func verify(_ app: URL, version expected: AppReleaseVersion) throws {
        guard try version(of: app) == expected else { throw AppUpdateError.invalidApp }
        try run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path])
    }
    static func prepare(archive: URL, destination: URL, version: AppReleaseVersion) throws -> PreparedAppUpdate {
        let manager = FileManager.default, target = destination.standardizedFileURL
        guard target.path == target.resolvingSymlinksInPath().path, try Self.version(of: target) < version,
              manager.isWritableFile(atPath: target.deletingLastPathComponent().path), manager.isWritableFile(atPath: target.path) else { throw AppUpdateError.cannotReplace }
        try AppUpdateArchive.validate(Data(contentsOf: archive, options: .mappedIfSafe))
        let work = target.deletingLastPathComponent().appendingPathComponent(".aipet-update-\(UUID().uuidString)", isDirectory: true)
        try manager.createDirectory(at: work, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        do {
            try run("/usr/bin/ditto", ["-x", "-k", archive.path, work.path])
            try verify(work.appendingPathComponent("AIpet.app"), version: version)
            return PreparedAppUpdate(work: work, destination: target, download: archive.deletingLastPathComponent(), version: version)
        } catch { try? manager.removeItem(at: work); throw error }
    }
    static func replace(staged: URL, destination: URL, version: AppReleaseVersion,
                        verify: (URL, AppReleaseVersion) throws -> Void = { try Self.verify($0, version: $1) },
                        move: (URL, URL) throws -> Void = { try FileManager.default.moveItem(at: $0, to: $1) },
                        launch: (URL) throws -> Void = { try Self.run("/usr/bin/open", ["-n", $0.path]) }) throws {
        let manager = FileManager.default
        guard try Self.version(of: destination) < version else { throw AppUpdateError.invalidApp }
        try verify(staged, version)
        let work = staged.deletingLastPathComponent(), backup = work.appendingPathComponent("previous.app")
        guard !manager.fileExists(atPath: backup.path) else { throw AppUpdateError.cannotReplace }
        try move(destination, backup)
        do {
            try move(staged, destination)
            try verify(destination, version)
            try launch(destination)
        } catch {
            // Keep the backup if rollback itself fails; never remove the only old copy.
            if manager.fileExists(atPath: destination.path) { try move(destination, work.appendingPathComponent("failed.app")) }
            try move(backup, destination)
            try? launch(destination)
            throw error
        }
        try manager.removeItem(at: backup)
    }
    static func runHelper(_ arguments: [String]) -> Int32 {
        do {
            guard arguments.count == 7, let parent = Int32(arguments[2]), parent > 1, parent != getpid(),
                  let version = AppReleaseVersion(arguments[5]) else { throw AppUpdateError.invalidApp }
            let work = URL(fileURLWithPath: arguments[3]).standardizedFileURL
            let destination = URL(fileURLWithPath: arguments[4]).standardizedFileURL
            let download = URL(fileURLWithPath: arguments[6]).standardizedFileURL
            guard work.lastPathComponent.hasPrefix(".aipet-update-"), work.deletingLastPathComponent().path == destination.deletingLastPathComponent().path,
                  work.path == work.resolvingSymlinksInPath().path, destination.path == destination.resolvingSymlinksInPath().path,
                  download.deletingLastPathComponent().path == downloads.standardizedFileURL.resolvingSymlinksInPath().path,
                  download.lastPathComponent.hasPrefix("download-"), download.path == download.resolvingSymlinksInPath().path else { throw AppUpdateError.invalidApp }
            let deadline = Date().addingTimeInterval(30)
            while kill(parent, 0) == 0 || errno != ESRCH {
                guard Date() < deadline else { throw AppUpdateError.parentRunning }
                Thread.sleep(forTimeInterval: 0.1)
            }
            try replace(staged: work.appendingPathComponent("AIpet.app"), destination: destination, version: version)
            try? FileManager.default.removeItem(at: work)
            try? FileManager.default.removeItem(at: download)
            return 0
        } catch {
            let app = NSApplication.shared; app.setActivationPolicy(.accessory); app.activate(ignoringOtherApps: true)
            let alert = NSAlert(); alert.messageText = "AIpet 업데이트를 완료하지 못했습니다."
            alert.informativeText = error.localizedDescription; alert.runModal()
            return 1
        }
    }
}
