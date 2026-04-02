set(CMAKE_SYSTEM_NAME Linux)
set(CMAKE_SYSTEM_PROCESSOR arm)

# Point these to your RUTOS/OpenWrt SDK toolchain.
# Download the SDK from Teltonika: https://wiki.teltonika-networks.com/view/Software_Development_Kit
# Example paths after extracting the SDK:
#   set(TOOLCHAIN_DIR "/opt/rutos-sdk/staging_dir/toolchain-arm_cortex-a7+neon-vfpv4_gcc-12.3.0_musl_eabi")
#   set(TARGET_DIR    "/opt/rutos-sdk/staging_dir/target-arm_cortex-a7+neon-vfpv4_musl_eabi")

set(TOOLCHAIN_DIR "$ENV{RUTOS_TOOLCHAIN_DIR}" CACHE PATH "Path to RUTOS toolchain")
set(TARGET_DIR    "$ENV{RUTOS_TARGET_DIR}"    CACHE PATH "Path to RUTOS target sysroot")

set(CMAKE_C_COMPILER   "${TOOLCHAIN_DIR}/bin/arm-openwrt-linux-muslgnueabi-gcc")
set(CMAKE_CXX_COMPILER "${TOOLCHAIN_DIR}/bin/arm-openwrt-linux-muslgnueabi-g++")
set(CMAKE_SYSROOT      "${TARGET_DIR}")

set(CMAKE_FIND_ROOT_PATH "${TARGET_DIR}")
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)

set(CMAKE_C_FLAGS   "${CMAKE_C_FLAGS}   -mcpu=cortex-a7 -mfpu=neon-vfpv4 -mfloat-abi=hard" CACHE STRING "" FORCE)
set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -mcpu=cortex-a7 -mfpu=neon-vfpv4 -mfloat-abi=hard" CACHE STRING "" FORCE)
