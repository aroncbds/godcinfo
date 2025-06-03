.PHONY: build run clean test tidy

# Default build target
build:
	go build -o godcinfo ./main.go

# Run the application
run: build
	./godcinfo

# Run with environment variables
run-with-env: build
	./godcinfo -url=$(VSPHERE_URL) -username=$(VSPHERE_USERNAME) -password=$(VSPHERE_PASSWORD) -datacenter=$(VSPHERE_DATACENTER)

# Clean build artifacts
clean:
	rm -f godcinfo

# Download and update dependencies
tidy:
	go mod tidy

# Install dependencies
deps:
	go mod download

# Test the application
test:
	go test -v ./...

# Help output
help:
	@echo "Available targets:"
	@echo "  build            - Build the application"
	@echo "  run              - Run the application (using environment variables or flags)"
	@echo "  run-with-env     - Run with environment variables"
	@echo "  clean            - Remove build artifacts"
	@echo "  tidy             - Update go.mod dependencies"
	@echo "  deps             - Download dependencies"
	@echo "  test             - Run tests"
	@echo "  help             - Show this help"
	@echo ""
	@echo "Environment variables:"
	@echo "  VSPHERE_URL      - URL of the vSphere server (required)"
	@echo "  VSPHERE_USERNAME - Username for vSphere authentication (required)"
	@echo "  VSPHERE_PASSWORD - Password for vSphere authentication (required)"
	@echo "  VSPHERE_DATACENTER - Datacenter name (optional, but usually required)" 