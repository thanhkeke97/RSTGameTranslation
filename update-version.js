// Node.js script to update version in HTML files based on latest_version_checker.json
const fs = require('fs');
const path = require('path');

// Path to the JSON file
const jsonFilePath = path.join(__dirname, 'media', 'latest_version_checker.json');

// Read the JSON file
try {
    const jsonData = fs.readFileSync(jsonFilePath, 'utf8');
    const versionInfo = JSON.parse(jsonData);
    
    // Get the version
    const version = versionInfo.latest_version;
    console.log(`Found version: ${version}`);
    
    // Find all HTML files in the current directory and subdirectories
    function findHtmlFiles(dir, fileList = []) {
        const files = fs.readdirSync(dir);
        
        files.forEach(file => {
            const filePath = path.join(dir, file);
            const stat = fs.statSync(filePath);
            
            if (stat.isDirectory()) {
                findHtmlFiles(filePath, fileList);
            } else if (path.extname(file).toLowerCase() === '.html') {
                fileList.push(filePath);
            }
        });
        
        return fileList;
    }
    
    const htmlFiles = findHtmlFiles(__dirname);
    
    // Update each HTML file
    htmlFiles.forEach(htmlFile => {
        console.log(`Processing: ${htmlFile}`);
        
        // Read the HTML file
        let htmlContent = fs.readFileSync(htmlFile, 'utf8');
        
        // Update version in version-badge div English version
        htmlContent = htmlContent.replace(
            /(<div\s+class="version-badge">)Version\s+[\d\.]+(<\/div>)/g,
            `$1Version ${version}$2`
        
        )
        // Update version in version-badge div Vietnamese version
        htmlContent = htmlContent.replace(
            /(<div\s+class="version-badge">)Phiên bản\s+[\d\.]+(<\/div>)/g,
            `$1Phiên bản ${version}$2`
        
        );
        
        // Update version in schema.org script
        htmlContent = htmlContent.replace(
            /("softwareVersion":\s*")[\d\.]+(")/g,
            `$1${version}$2`
        );
        
        // Write the updated content back to the file
        fs.writeFileSync(htmlFile, htmlContent);
        
        console.log(`Updated version in ${path.basename(htmlFile)}`);
    });
    
    console.log('Version update complete');
    
} catch (error) {
    console.error(`Error updating version: ${error.message}`);
}